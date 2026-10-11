using System.Threading.Tasks;
using Dapper;
using LoanApplication.Api.Data;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// Integration tests that run against a real PostgreSQL 18 database via Testcontainers.
/// These tests verify EF Core mappings, migration application, timestamptz UTC enforcement,
/// Restrict delete behavior on the audit trail, and Dapper SQL behavior against live PG.
/// </summary>
[Collection("PostgresCollection")]
public sealed class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private AppDbContext _db = null!;

    public PostgresIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _testSuffix = Guid.NewGuid().ToString("N")[..8];
    }

    private readonly string _testSuffix;

    public Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Clean up test data after each test - use correct table names from migration
        await _db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"StatusHistories\", \"Loans\", \"Applicants\", \"Users\" RESTART IDENTITY CASCADE");
        await _db.DisposeAsync();
    }

    private async Task<(Guid applicantId, Guid userId)> CreateTestApplicantWithUserAsync(string? email = null)
    {
        var uniqueEmail = email is null
            ? $"test-{_testSuffix}@example.com"
            : $"{email.Split('@')[0]}-{_testSuffix}@example.com";
        var user = new User(uniqueEmail, "hashed", UserRole.Applicant);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var applicant = new Applicant(user.Id, "Test User", new DateOnly(1990, 1, 1), 5000m);
        _db.Applicants.Add(applicant);
        await _db.SaveChangesAsync();

        return (applicant.Id, user.Id);
    }

    private async Task<Guid> CreateTestUserAsync(string? email = null, UserRole role = UserRole.Applicant)
    {
        var uniqueEmail = email is null
            ? $"user-{_testSuffix}@example.com"
            : $"{email.Split('@')[0]}-{_testSuffix}@example.com";
        var user = new User(uniqueEmail, "hashed", role);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task SubmitLoan_PersistsInitialHistoryEntry()
    {
        // Arrange
        var (applicantId, userId) = await CreateTestApplicantWithUserAsync();

        var now = DateTime.UtcNow;
        var loan = Loan.Submit(applicantId, userId, 10000m, 12, "Test loan", now);
        _db.Loans.Add(loan);
        await _db.SaveChangesAsync();

        // Act - reload from database
        var reloaded = await _db.Loans
            .Include(l => l.History)
            .FirstAsync(l => l.Id == loan.Id);

        // Assert
        Assert.Single(reloaded.History);
        Assert.Equal(LoanStatus.Submitted, reloaded.History.First().ToStatus);
        Assert.Equal(now, reloaded.CreatedAt);
    }

    [Fact]
    public async Task StartReview_AppendsSecondHistoryEntry()
    {
        // Arrange
        var (applicantId, applicantUserId) = await CreateTestApplicantWithUserAsync();
        var officerUserId = await CreateTestUserAsync("officer@example.com", UserRole.LoanOfficer);

        var now = DateTime.UtcNow;
        var loan = Loan.Submit(applicantId, applicantUserId, 10000m, 12, "Test loan", now);
        _db.Loans.Add(loan);
        await _db.SaveChangesAsync();

        // Act - transition to UnderReview
        var reviewTime = now.AddMinutes(5);
        loan.StartReview(officerUserId, reviewTime);
        await _db.SaveChangesAsync();

        // Reload from database
        var reloaded = await _db.Loans
            .Include(l => l.History)
            .FirstAsync(l => l.Id == loan.Id);

        // Assert
        Assert.Equal(2, reloaded.History.Count);
        Assert.Equal(LoanStatus.Submitted, reloaded.History.First().ToStatus);
        Assert.Equal(LoanStatus.UnderReview, reloaded.History.Last().ToStatus);
        Assert.Equal(LoanStatus.UnderReview, reloaded.Status);
    }

    [Fact]
    public async Task Migrations_AreAppliedWithNoPendingMigrations()
    {
        // The fixture migrates on startup; this asserts the schema is actually current
        // rather than relying on fixture success as proof.
        var pending = await _db.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
    }

    [Fact]
    public async Task Migrations_CreateExpectedSchema()
    {
        // Verifies the InitialCreate migration built the expected schema on real PG.
        var tables = new List<string>();
        await using (var connection = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await connection.OpenAsync();
            var rows = await connection.QueryAsync<string>(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                """);
            tables.AddRange(rows);
        }

        Assert.Contains("Users", tables);
        Assert.Contains("Applicants", tables);
        Assert.Contains("Loans", tables);
        Assert.Contains("StatusHistories", tables);
    }

    [Fact]
    public async Task ReportService_GroupsLoansByStatusOnLivePostgres()
    {
        // Arrange - seed data via EF
        var (applicantId1, applicantUserId1) = await CreateTestApplicantWithUserAsync("user1@example.com");
        var (applicantId2, applicantUserId2) = await CreateTestApplicantWithUserAsync("user2@example.com");
        var officerUserId = await CreateTestUserAsync("officer@example.com", UserRole.LoanOfficer);

        var now = DateTime.UtcNow;
        var loan1 = Loan.Submit(applicantId1, applicantUserId1, 10000m, 12, "Loan 1", now);
        var loan2 = Loan.Submit(applicantId2, applicantUserId2, 25000m, 24, "Loan 2", now);
        var loan3 = Loan.Submit(applicantId1, applicantUserId1, 5000m, 6, "Loan 3", now);

        // Transition loan1 to Approved, loan2 to Rejected
        loan1.StartReview(officerUserId, now.AddMinutes(10));
        loan1.Approve(officerUserId, now.AddMinutes(20), "Approved");
        
        loan2.StartReview(officerUserId, now.AddMinutes(10));
        loan2.Reject(officerUserId, now.AddMinutes(20), "Insufficient income");

        // loan3 stays Submitted

        _db.Loans.AddRange(loan1, loan2, loan3);
        await _db.SaveChangesAsync();

        // Act - use ReportService with live connection
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _fixture.ConnectionString
            })
            .Build();

        var reportService = new ReportService(config);
        var summary = await reportService.GetStatusSummaryAsync(CancellationToken.None);

        // Assert
        // Expect: Submitted=1 (5000), Approved=1 (10000), Rejected=1 (25000)
        // UnderReview may not appear (0 rows in DB = not in GROUP BY result)
        
        var submitted = summary.FirstOrDefault(s => s.Status == "Submitted");
        var approved = summary.FirstOrDefault(s => s.Status == "Approved");
        var rejected = summary.FirstOrDefault(s => s.Status == "Rejected");

        Assert.NotNull(submitted);
        Assert.Equal(1L, submitted!.Count);
        Assert.Equal(5000m, submitted.TotalAmount);

        Assert.NotNull(approved);
        Assert.Equal(1L, approved!.Count);
        Assert.Equal(10000m, approved.TotalAmount);

        Assert.NotNull(rejected);
        Assert.Equal(1L, rejected!.Count);
        Assert.Equal(25000m, rejected.TotalAmount);

        // Verify DTO materializes correctly with long Count and decimal TotalAmount
        Assert.IsType<long>(submitted.Count);
        Assert.IsType<decimal>(submitted.TotalAmount);
    }

    [Fact]
    public async Task ReportService_ReturnsEmptyListWhenNoLoans()
    {
        // Arrange - no loans in database (DisposeAsync truncates before each test)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _fixture.ConnectionString
            })
            .Build();

        var reportService = new ReportService(config);

        // Act
        var summary = await reportService.GetStatusSummaryAsync(CancellationToken.None);

        // Assert
        Assert.Empty(summary);
    }

    [Fact]
    public async Task StatusHistory_RestrictDeleteProtectsAuditTrail()
    {
        // Arrange
        var (applicantId, userId) = await CreateTestApplicantWithUserAsync();

        var now = DateTime.UtcNow;
        var loan = Loan.Submit(applicantId, userId, 10000m, 12, "Test loan", now);
        _db.Loans.Add(loan);
        await _db.SaveChangesAsync();

        // Get the loan ID for raw SQL test
        var loanId = loan.Id;

        // Act & Assert - try to delete the loan via raw SQL, should fail due to FK Restrict
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await connection.ExecuteAsync("DELETE FROM \"Loans\" WHERE \"Id\" = @id", new { id = loanId });
        });

        // 23001 is restrict_violation, raised specifically by ON DELETE RESTRICT.
        // Asserting it exactly (rather than also accepting 23503 foreign_key_violation)
        // is what proves the audit trail is protected by Restrict and not NO ACTION.
        Assert.Equal("23001", ex.SqlState);
    }

    [Fact]
    public async Task LoanStatus_StoredAsTextNotInteger()
    {
        // Verifies LoanStatus is stored as varchar text in the database, not the
        // underlying integer enum value. We check the column's data_type in
        // information_schema, which is immune to EF's conversion layer.
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var dataType = await connection.ExecuteScalarAsync<string>(
            """
            SELECT data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'Loans'
              AND column_name = 'Status'
            """);

        // Assert - status column must be text (character varying), not integer
        Assert.Equal("character varying", dataType);
    }
}