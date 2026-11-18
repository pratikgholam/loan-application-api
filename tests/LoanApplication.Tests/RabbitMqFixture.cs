using Testcontainers.RabbitMq;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// xUnit collection fixture that spins up a real RabbitMQ container (management image)
/// once per test run for tests that exercise the broker.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container =
        new RabbitMqBuilder("rabbitmq:4-management").Build();

    public string Host => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(RabbitMqBuilder.RabbitMqPort);
    public string UserName => RabbitMqBuilder.DefaultUsername;
    public string Password => RabbitMqBuilder.DefaultPassword;

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Ties broker tests to a real RabbitMQ and a real PostgreSQL container.</summary>
[CollectionDefinition("BrokerCollection")]
public class BrokerCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>
{
    // This class has no code, and is never created.
}
