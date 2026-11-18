using System.Text.Json;
using LoanApplication.Api.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace LoanApplication.Api.Services;

/// <summary>
/// Publishes directly to RabbitMQ with publisher confirms. A publish counts as
/// successful only when the broker confirms it (nacks and returns throw).
/// </summary>
public sealed class RabbitMqLoanSubmittedPublisher : ILoanSubmittedPublisher
{
    private const int MaxAttempts = 4; // initial attempt + 3 retries
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqLoanSubmittedPublisher> _logger;

    public RabbitMqLoanSubmittedPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqLoanSubmittedPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(LoanSubmitted message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, LoanSubmitted.JsonOptions);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await PublishOnceAsync(body, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Publishing LoanSubmitted for {LoanId} failed (attempt {Attempt}/{MaxAttempts}).",
                    message.LoanId, attempt, MaxAttempts);

                if (attempt < MaxAttempts)
                    await Task.Delay(RetryDelay, ct);
            }
        }

        _logger.LogError(
            "Gave up publishing LoanSubmitted for {LoanId} after {MaxAttempts} attempts; the loan stays saved and unassigned.",
            message.LoanId, MaxAttempts);
    }

    private async Task PublishOnceAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        // A connection per publish keeps recovery simple; the API's submission rate is low.
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            ct);

        await channel.QueueDeclareAsync(
            queue: _options.QueueName, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: _options.QueueName,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json" },
            body: body,
            cancellationToken: ct);
    }
}
