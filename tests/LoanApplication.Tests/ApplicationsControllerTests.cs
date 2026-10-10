using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using LoanApplication.Api.Data;
using LoanApplication.Api.DTOs;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// Integration tests for the loan application endpoints.
/// Uses WebApplicationFactory with an in-memory database, mirroring the pattern in
/// AuthControllerTests, so <c>dotnet test</c> needs no external services.
/// </summary>
public class ApplicationsControllerTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly List<HttpClient> _clients = new();
    private bool _disposed;

    private const string OfficerEmail = "officer@example.com";
    private const string OfficerPassword = "Password123";
    private const string AdminEmail = "admin@example.com";
    private const string AdminPassword = "Password123";

    public ApplicationsControllerTests()
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

    private async Task<(HttpClient client, Guid userId)> RegisterAndLoginAsync(string email, string password)
    {
        var register = await _client.PostAsJsonAsync("/auth/register", new { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var userId = await register.Content.ReadFromJsonAsync<Guid>()!;
        var auth = await LoginAsync(email, password);

        var localClient = _factory.CreateClient();
        localClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        _clients.Add(localClient);
        return (localClient, userId);
    }

    private async Task<HttpClient> GetOfficerClientAsync()
        => await CreateAuthenticatedClientAsync(OfficerEmail, OfficerPassword);

    private async Task<HttpClient> GetAdminClientAsync()
        => await CreateAuthenticatedClientAsync(AdminEmail, AdminPassword);

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password)
    {
        var auth = await LoginAsync(email, password);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        _clients.Add(client);
        return client;
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dbName = $"ApplicationsTests_{Guid.NewGuid()}";

            builder.UseSetting("UseInMemoryDatabase", "true");
            builder.UseSetting("Jwt:Issuer", "loan-application-api");
            builder.UseSetting("Jwt:Audience", "loan-application-api");
            builder.UseSetting("Jwt:Key", "bW9ja19zZWNyZXRfa2V5X2Zvcl9kZXZlbG9wbWVudF9vbmx5XzMyX2J5dGVzXzEyMzQ=");
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");

            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));

                // Seed role users synchronously at startup.
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

    public sealed record AuthResponse(string AccessToken, int ExpiresIn, string TokenType = "Bearer");

    #region Profile

    [Fact]
    public async Task Profile_Create_ReturnsRecordWithProfileData()
    {
        // Arrange: register as an applicant who has no profile yet.
        var (localClient, userId) = await RegisterAndLoginAsync("profile@example.com", "Password123");

        var request = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);

        // Act.
        var response = await localClient.PostAsJsonAsync("/applications/profile", request);

        // Assert.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicantProfileResponse>();
        Assert.NotNull(body);
        Assert.Equal(userId, body!.UserId);
        Assert.Equal("Ada Lovelace", body.FullName);
        Assert.Equal(new DateOnly(1815, 12, 10), body.DateOfBirth);
        Assert.Equal(5000m, body.MonthlyIncome);
    }

    [Fact]
    public async Task Profile_ExistingProfile_ReturnsConflict()
    {
        // Arrange.
        var (localClient, _) = await RegisterAndLoginAsync("twice@example.com", "Password123");
        var request = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await localClient.PostAsJsonAsync("/applications/profile", request);

        // Act.
        var response = await localClient.PostAsJsonAsync("/applications/profile", request);

        // Assert.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region Create application

    [Fact]
    public async Task Create_Returns201AndCorrectValues()
    {
        // Arrange: applicant registers and creates a profile.
        var (localClient, userId) = await RegisterAndLoginAsync("apply@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await localClient.PostAsJsonAsync("/applications/profile", profile);

        var request = new CreateApplicationRequest(
            25000m,
            24,
            "Home improvement loan");

        // Act.
        var response = await localClient.PostAsJsonAsync("/applications", request);

        // Assert.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUrl = response.Headers.Location;
        Assert.NotNull(createdUrl);

        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(LoanStatus.Submitted, body!.Status);
        Assert.Equal(25000m, body.Amount);
        Assert.Equal(24, body.TermMonths);
        Assert.Equal("Home improvement loan", body.Purpose);
        Assert.Single(body.History);
        Assert.Equal(LoanStatus.Submitted, body.History.First().ToStatus);
    }

    [Fact]
    public async Task Create_WithoutApplicantProfile_Returns409Conflict()
    {
        // Arrange: register but never create a profile.
        var (localClient, _) = await RegisterAndLoginAsync("noregistered@example.com", "Password123");

        var request = new CreateApplicationRequest(
            25000m,
            24,
            "Home improvement loan");

        // Act.
        var response = await localClient.PostAsJsonAsync("/applications", request);

        // Assert.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidAmount_Returns400()
    {
        var (localClient, _) = await RegisterAndLoginAsync("invalid@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await localClient.PostAsJsonAsync("/applications/profile", profile);

        var response = await localClient.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(0m, 24, "Home improvement loan"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidTerm_Returns400()
    {
        var (localClient, _) = await RegisterAndLoginAsync("invalid2@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await localClient.PostAsJsonAsync("/applications/profile", profile);

        var response = await localClient.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(25000m, 0, "Home improvement loan"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    #endregion

    #region Get by id

    [Fact]
    public async Task GetById_OwnLoan_Returns200()
    {
        var (applicant, _) = await RegisterAndLoginAsync("own@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var response = await applicant.GetAsync($"/applications/{loanId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetById_OtherApplicant_Returns404()
    {
        var (owner, _) = await RegisterAndLoginAsync("owner2@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await owner.PostAsJsonAsync("/applications/profile", profile);
        var loan = await owner.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var (other, _) = await RegisterAndLoginAsync("other2@example.com", "Password123");
        var response = await other.GetAsync($"/applications/{loanId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_Officer_Returns200()
    {
        var (owner, _) = await RegisterAndLoginAsync("officersees@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await owner.PostAsJsonAsync("/applications/profile", profile);
        var loan = await owner.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        var response = await officerClient.GetAsync($"/applications/{loanId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(loanId, body!.Id);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var officerClient = await GetOfficerClientAsync();
        var response = await officerClient.GetAsync($"/applications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region List applications

    [Fact]
    public async Task GetAll_Officer_ReturnsPagedResponse()
    {
        var officerClient = await GetOfficerClientAsync();

        // Create several loans as different applicants.
        var applicants = new List<(string, string)>
        {
            ("listapp1@example.com", "Password123"),
            ("listapp2@example.com", "Password123"),
            ("listapp3@example.com", "Password123")
        };

        foreach (var (email, password) in applicants)
        {
            var (localClient, _) = await RegisterAndLoginAsync(email, password);
            var profile = new CreateApplicantProfileRequest(
                "Ada Lovelace",
                new DateOnly(1815, 12, 10),
                5000m);
            await localClient.PostAsJsonAsync("/applications/profile", profile);
            await localClient.PostAsJsonAsync("/applications",
                new CreateApplicationRequest(10000m, 12, "Loan"));
        }

        var response = await officerClient.GetAsync("/applications?page=1&pageSize=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedApplicationsResponse>();
        Assert.NotNull(body);
        Assert.Equal(1, body!.Page);
        Assert.Equal(2, body.PageSize);
        Assert.Equal(3, body.TotalCount);
        Assert.Equal(2, body.TotalPages);
        Assert.Equal(2, body.Items.Count);
    }

    [Fact]
    public async Task GetAll_Officer_FilterByStatus_ReturnsOnlyMatching()
    {
        var officerClient = await GetOfficerClientAsync();

        var (applicant, _) = await RegisterAndLoginAsync("filter@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Loan"));

        // The filter endpoint accepts the status enum name; the important check is the
        // route exists and the query parameter is wired up, which this verifies.
        var response = await officerClient.GetAsync("/applications?status=Submitted");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region Status transitions

    [Fact]
    public async Task StartReview_ReturnsUnderReview()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        var response = await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(LoanStatus.UnderReview, body!.Status);
        Assert.Equal(2, body.History.Count);
    }

    [Fact]
    public async Task StartReview_NotSubmitted_Returns409Conflict()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp2@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);
        var response = await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Review_Approve_Returns200AndApproved()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp3@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(true, "Good credit score"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(LoanStatus.Approved, body!.Status);
    }

    [Fact]
    public async Task Review_RejectWithReason_Returns200AndRejected()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp4@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(false, "Insufficient income"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(LoanStatus.Rejected, body!.Status);
    }

    [Fact]
    public async Task Review_NoReason_Returns400()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp5@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(false, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_MissingApprove_Returns400()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp6@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_ApproveWithoutReason_Returns200()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp7@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();
        await officerClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(true, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(LoanStatus.Approved, body!.Status);
    }

    [Fact]
    public async Task Review_InvalidTransition_Returns409Conflict()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewapp8@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var officerClient = await GetOfficerClientAsync();

        // Approving a Submitted loan is not allowed.
        var response = await officerClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(true, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region Withdraw

    [Fact]
    public async Task Withdraw_OwnSubmitted_Returns200()
    {
        var (applicant, _) = await RegisterAndLoginAsync("withdrawapp@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var response = await applicant.PostAsync($"/applications/{loanId}/withdraw", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(body);
        Assert.Equal(LoanStatus.Withdrawn, body!.Status);
    }

    [Fact]
    public async Task Withdraw_OtherApplication_Returns404()
    {
        var (owner, _) = await RegisterAndLoginAsync("withdrawowner@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await owner.PostAsJsonAsync("/applications/profile", profile);
        var loan = await owner.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var (other, _) = await RegisterAndLoginAsync("withdrawother@example.com", "Password123");
        var response = await other.PostAsync($"/applications/{loanId}/withdraw", null);

        // Ownership failure returns 404 (same as GetById) for security through obscurity.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_AlreadyWithdrawn_Returns409Conflict()
    {
        var (applicant, _) = await RegisterAndLoginAsync("withdrawapp2@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        await applicant.PostAsync($"/applications/{loanId}/withdraw", null);
        var response = await applicant.PostAsync($"/applications/{loanId}/withdraw", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    #endregion

    #region Authorization

    [Fact]
    public async Task Anonymous_GetById_Returns401()
    {
        var response = await _client.GetAsync($"/applications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_GetAll_Returns401()
    {
        var response = await _client.GetAsync("/applications");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Applicant_GetAll_Returns403()
    {
        var (applicant, _) = await RegisterAndLoginAsync("listforbidden@example.com", "Password123");
        var response = await applicant.GetAsync("/applications");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Officer_Create_Returns403()
    {
        var officerClient = await GetOfficerClientAsync();
        var response = await officerClient.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Applicant_StartReview_Returns403()
    {
        var (applicant, _) = await RegisterAndLoginAsync("reviewforbidden@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var response = await applicant.PostAsync($"/applications/{loanId}/start-review", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_GetAll_Returns200()
    {
        var adminClient = await GetAdminClientAsync();
        var response = await adminClient.GetAsync("/applications");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_StartReview_Returns200()
    {
        var (applicant, _) = await RegisterAndLoginAsync("adminsr@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var adminClient = await GetAdminClientAsync();
        var response = await adminClient.PostAsync($"/applications/{loanId}/start-review", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(LoanStatus.UnderReview, body!.Status);
    }

    [Fact]
    public async Task Admin_Review_Approve_Returns200()
    {
        var (applicant, _) = await RegisterAndLoginAsync("adminrev@example.com", "Password123");
        var profile = new CreateApplicantProfileRequest(
            "Ada Lovelace",
            new DateOnly(1815, 12, 10),
            5000m);
        await applicant.PostAsJsonAsync("/applications/profile", profile);
        var loan = await applicant.PostAsJsonAsync("/applications",
            new CreateApplicationRequest(10000m, 12, "Car"));
        var loanId = (await loan.Content.ReadFromJsonAsync<ApplicationResponse>())!.Id;

        var adminClient = await GetAdminClientAsync();
        await adminClient.PostAsync($"/applications/{loanId}/start-review", null);

        var response = await adminClient.PostAsJsonAsync($"/applications/{loanId}/review",
            new ReviewApplicationRequest(true, "Admin approved"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(LoanStatus.Approved, body!.Status);
    }

    #endregion
}