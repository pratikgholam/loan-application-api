using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using LoanApplication.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// xUnit collection fixture that spins up a real PostgreSQL container (PostgreSQL 18 Alpine)
/// and runs EF Core migrations against it. The container is started once per test run
/// and shared across all tests in the <see cref="PostgresCollection"/> collection.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgresFixture()
    {
        var db = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "loanapp";
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "loanapp";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "loanapp";

        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase(db)
            .WithUsername(user)
            .WithPassword(password)
            .WithPortBinding(5432, true)
            .Build();
    }

    /// <summary>
    /// The connection string for the test database. Valid after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Creates a new <see cref="AppDbContext"/> configured to use the test database.
    /// Each caller should dispose the context when done.
    /// </summary>
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Runs the EF Core migrations against the test database. Use
    /// <see cref="Database.GetPendingMigrations"/> with the context from
    /// <see cref="CreateDbContext"/> to assert the schema is current.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Migrate (not EnsureCreated) so the real InitialCreate migration is what
        // builds the schema under test.
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

/// <summary>
/// xUnit collection definition that ties tests to the shared <see cref="PostgresFixture"/>.
/// Tests that need a real PostgreSQL database should be placed in this collection:
/// <code>[Collection("PostgresCollection")]</code>
/// </summary>
[CollectionDefinition("PostgresCollection")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture interfaces.
}