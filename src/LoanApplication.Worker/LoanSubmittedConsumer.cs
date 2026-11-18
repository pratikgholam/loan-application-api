using System.Text.Json;
using LoanApplication.Api.Messaging;
using LoanApplication.Api.Services;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LoanApplication.Worker;

/// <summary>
/// Consumes one LoanSubmitted message at a time, assigns the loan, then acknowledges.
/// A failed assignment is nacked and redelivered; an unmatched loan type is logged
/// and acknowledged so one loan cannot block the queue.
/// </summary>
public sealed class LoanSubmittedConsumer : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<LoanSubmittedConsumer> _logger;

    public LoanSubmittedConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<LoanSubmittedConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RabbitMQ consumer stopped; reconnecting in {Delay}.", ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: _options.QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        // One message at a time keeps assignment order deterministic.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => OnMessageAsync(channel, args, ct);
        await channel.BasicConsumeAsync(
            queue: _options.QueueName, autoAck: false, consumerTag: string.Empty,
            noLocal: false, exclusive: false, arguments: null, consumer: consumer,
            cancellationToken: ct);

        _logger.LogInformation("Waiting for LoanSubmitted messages on {Queue}.", _options.QueueName);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken ct)
    {
        LoanSubmitted? message;
        try
        {
            message = JsonSerializer.Deserialize<LoanSubmitted>(args.Body.Span, LoanSubmitted.JsonOptions);
        }
        catch (JsonException ex)
        {
            // A malformed message can never succeed; drop it instead of blocking the queue.
            _logger.LogError(ex, "Acknowledging malformed LoanSubmitted message.");
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        if (message is null)
        {
            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<LoanAssignmentHandler>()
                .HandleAsync(message, ct);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged so the broker redelivers it.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assigning loan {LoanId} failed; requeueing the message.", message.LoanId);
            await Task.Delay(FailureDelay, ct);
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }
}
