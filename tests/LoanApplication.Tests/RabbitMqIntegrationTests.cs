using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LoanApplication.Api.Data;
using LoanApplication.Api.DTOs;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Messaging;
using LoanApplication.Api.Services;
using LoanApplication.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// Roundtrip through a real broker and database: submit through the API, the API
/// publishes, the worker assigns an officer, then the message is acknowledged.
/// </summary>
[Collection("BrokerCollection")]
public class RabbitMqIntegrationTests
{
    private const string JwtKey = "bW9ja19zZWNyZXRfa2V5X2Zvcl9kZXZlbG9wbWVudF9vbmx5XzMyX2J5dGVzXzEyMzQ=";
    private readonly PostgresFixture _postgres;
    private readonly RabbitMqFixture _rabbit;

    public RabbitMqIntegrationTests(PostgresFixture postgres, RabbitMqFixture rabbit)
    {
        _postgres = postgres;
        _rabbit = rabbit;
    }

    [Fact]
    public async Task Submit_Publishes_WorkerAssigns_AndAcks()
    {
        var officerId = await SeedOfficerAsync();

        using var api = new BrokerApiFactory(_postgres.ConnectionString, _rabbit);
        using var client = api.CreateClient();
        const string email = "broker-applicant@example.com";

        var register = await client.PostAsJsonAsync("/auth/register", new { Email = email, Password = "Password123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var auth = await (await client.PostAsJsonAsync("/auth/login", new { Email = email, Password = "Password123" }))
            .Content.ReadFromJsonAsync<ApplicationsControllerTests.AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        await client.PostAsJsonAsync("/applications/profile",
            new CreateApplicantProfileRequest("Ada Lovelace", new DateOnly(1815, 12, 10), 5000m));

        using var worker = BuildWorkerHost();
        await worker.StartAsync();
        try
        {
            var response = await client.PostAsJsonAsync("/applications",
                new CreateApplicationRequest(10000m, 12, "Car", LoanType.Auto));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var loanId = (await response.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

            await WaitUntilAsync(async () =>
            {
                await using var db = _postgres.CreateDbContext();
                var loan = await db.Loans.AsNoTracking().SingleOrDefaultAsync(l => l.Id == loanId);
                return loan?.AssignedOfficerUserId == officerId;
            }, TimeSpan.FromSeconds(20));
        }
        finally
        {
            await worker.StopAsync();
        }

        // The consumer is gone: had the message not been acknowledged, the broker
        // would have requeued it and MessageCount would be 1.
        await Task.Delay(500);
        await AssertQueueEmptyAsync();
    }

    private async Task<Guid> SeedOfficerAsync()
    {
        await using var db = _postgres.CreateDbContext();
        var officer = new User(
            $"auto-officer-{Guid.NewGuid()}@example.com", "hash", UserRole.LoanOfficer, LoanType.Auto);
        db.Users.Add(officer);
        await db.SaveChangesAsync();
        return officer.Id;
    }

    private IHost BuildWorkerHost()
    {
        var builder = new HostApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _postgres.ConnectionString,
            ["RabbitMq:Host"] = _rabbit.Host,
            ["RabbitMq:Port"] = _rabbit.Port.ToString(),
            ["RabbitMq:UserName"] = _rabbit.UserName,
            ["RabbitMq:Password"] = _rabbit.Password
        });

        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_postgres.ConnectionString));
        builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection("RabbitMq"));
        builder.Services.AddScoped<LoanAssignmentHandler>();
        builder.Services.AddHostedService<LoanSubmittedConsumer>();

        return builder.Build();
    }

    private async Task AssertQueueEmptyAsync()
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbit.Host,
            Port = _rabbit.Port,
            UserName = _rabbit.UserName,
            Password = _rabbit.Password
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var queue = await channel.QueueDeclareAsync(
            queue: LoanSubmitted.QueueName, durable: true, exclusive: false, autoDelete: false,
            passive: true);

        Assert.Equal(0u, queue.MessageCount);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }

        Assert.Fail("Timed out waiting for the worker to assign the loan.");
    }

    private sealed class BrokerApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly RabbitMqFixture _rabbit;

        public BrokerApiFactory(string connectionString, RabbitMqFixture rabbit)
        {
            _connectionString = connectionString;
            _rabbit = rabbit;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", _connectionString);
            builder.UseSetting("Jwt:Issuer", "loan-application-api");
            builder.UseSetting("Jwt:Audience", "loan-application-api");
            builder.UseSetting("Jwt:Key", JwtKey);
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("RabbitMq:Host", _rabbit.Host);
            builder.UseSetting("RabbitMq:Port", _rabbit.Port.ToString());
            builder.UseSetting("RabbitMq:UserName", _rabbit.UserName);
            builder.UseSetting("RabbitMq:Password", _rabbit.Password);
            builder.UseSetting("ASPNETCORE_URLS", "http://localhost");
            builder.ConfigureServices(services =>
            {
                services.Configure<LoggerFilterOptions>(options =>
                {
                    options.AddFilter("Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware", LogLevel.Error);
                });
            });
        }
    }
}
