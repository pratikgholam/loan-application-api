using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LoanApplication.Api.Data;
using LoanApplication.Api.DTOs;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// Tests for GET /reports/status-summary.
///
/// The real <see cref="ReportService"/> executes hand-written SQL through Dapper against
/// PostgreSQL, which the EF InMemory test setup cannot serve. These tests swap in a fake
/// <see cref="IReportService"/> so authorization and routing stay covered by <c>dotnet test</c>
/// with no external services. The SQL and the real service are covered by the PostgreSQL-backed
/// integration tests.
/// </summary>
public class ReportsControllerTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<HttpClient> _clients = new();
    private bool _disposed;

    private const string OfficerEmail = "reportsofficer@example.com";
    private const string OfficerPassword = "Password123";
    private const string AdminEmail = "reportsadmin@example.com";
    private const string AdminPassword = "Password123";

    private static readonly StatusSummaryDto[] Summary =
    [
        new("Submitted", 3, 25000m),
        new("Approved", 1, 5000m)
    ];

    public ReportsControllerTests()
    {
        _factory = new TestFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var c in _clients)
                c.Dispose();
            _client.Dispose();
            _factory.Dispose();
            _disposed = true;
        }
    }

    private async Task<AuthResponse> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var auth = await LoginAsync(email, password);

        var localClient = _factory.CreateClient();
        localClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        _clients.Add(localClient);
        return localClient;
    }

    private async Task<HttpClient> GetOfficerClientAsync()
        => await CreateAuthenticatedClientAsync(OfficerEmail, OfficerPassword);

    private async Task<HttpClient> GetAdminClientAsync()
        => await CreateAuthenticatedClientAsync(AdminEmail, AdminPassword);

    /// <summary>Registers an applicant and returns an authenticated client for them.</summary>
    private async Task<HttpClient> GetApplicantClientAsync()
    {
        var register = await _client.PostAsJsonAsync("/auth/register",
            new { Email = "reportsapplicant@example.com", Password = "Password123" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        return await CreateAuthenticatedClientAsync("reportsapplicant@example.com", "Password123");
    }

    #region Happy path

    [Fact]
    public async Task StatusSummary_LoanOfficer_ReturnsSummary()
    {
        // Arrange.
        var officer = await GetOfficerClientAsync();

        // Act.
        var response = await officer.GetAsync("/reports/status-summary");

        // Assert.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<StatusSummaryDto>>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
        Assert.Equal("Submitted", body[0].Status);
        Assert.Equal(3, body[0].Count);
        Assert.Equal(25000m, body[0].TotalAmount);
        Assert.Equal("Approved", body[1].Status);
    }

    [Fact]
    public async Task StatusSummary_Admin_ReturnsSummary()
    {
        // Arrange.
        var admin = await GetAdminClientAsync();

        // Act.
        var response = await admin.GetAsync("/reports/status-summary");

        // Assert.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<StatusSummaryDto>>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task StatusSummary_Applicant_Returns403()
    {
        // Arrange: applicants must not see portfolio-wide totals.
        var applicant = await GetApplicantClientAsync();

        // Act.
        var response = await applicant.GetAsync("/reports/status-summary");

        // Assert.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StatusSummary_Anonymous_Returns401()
    {
        // Act: no bearer token.
        var response = await _client.GetAsync("/reports/status-summary");

        // Assert.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dbName = $"ReportsTests_{Guid.NewGuid()}";

            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("Jwt:Issuer", "loan-application-api");
            builder.UseSetting("Jwt:Audience", "loan-application-api");
            builder.UseSetting("Jwt:Key", "bW9ja19zZWNyZXRfa2V5X2Zvcl9kZXZlbG9wbWVudF9vbmx5XzMyX2J5dGVzXzEyMzQ=");
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");

            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));

                // The real ReportService opens an Npgsql connection, which cannot be faked by the
                // in-memory provider. Substituting it keeps these tests to authorization only.
                services.RemoveAll<IReportService>();
                services.AddScoped<IReportService, FakeReportService>();

                SeedUsersStatic(services);
            });
        }

        public static void SeedUsersStatic(IServiceCollection services)
        {
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            db.Users.AddRange(
                new User(OfficerEmail, hasher.HashPassword(OfficerPassword), UserRole.LoanOfficer),
                new User(AdminEmail, hasher.HashPassword(AdminPassword), UserRole.Admin));
            db.SaveChanges();
        }
    }

    private sealed class FakeReportService : IReportService
    {
        public Task<IReadOnlyList<StatusSummaryDto>> GetStatusSummaryAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<StatusSummaryDto>>(Summary);
    }

    public sealed record AuthResponse(string AccessToken, int ExpiresIn, string TokenType = "Bearer");
}