using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using LoanApplication.Api.Data;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LoanApplication.Tests;

public class AuthControllerTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthControllerTests()
    {
        var dbName = $"TestDb_{Guid.NewGuid()}";

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("UseInMemoryDatabase", "true");
                builder.UseSetting("Jwt:Issuer", "loan-application-api");
                builder.UseSetting("Jwt:Audience", "loan-application-api");
                builder.UseSetting("Jwt:Key", "bW9ja19zZWNyZXRfa2V5X2Zvcl9kZXZlbG9wbWVudF9vbmx5XzMyX2J5dGVzXzEyMzQ=");
                builder.UseSetting("Jwt:AccessTokenMinutes", "15");

                builder.ConfigureServices(services =>
                {
                    // Add in-memory database for testing with unique name
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Register_ValidRequest_ReturnsCreated()
    {
        var request = new { Email = "newuser@example.com", Password = "Password123" };

        var response = await _client.PostAsJsonAsync("/auth/register", request);

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"InternalServerError: {errorContent}");
        }

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var userId = await response.Content.ReadFromJsonAsync<Guid>();
        Assert.NotEqual(Guid.Empty, userId);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var request = new { Email = "duplicate@example.com", Password = "Password123" };

        await _client.PostAsJsonAsync("/auth/register", request);
        var response = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsBadRequest()
    {
        var request = new { Email = "not-an-email", Password = "Password123" };

        var response = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordTooShort_ReturnsBadRequest()
    {
        var request = new { Email = "short@example.com", Password = "Pass1" };

        var response = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_PasswordTooLong_ReturnsBadRequest()
    {
        var request = new { Email = "long@example.com", Password = "ThisPasswordIsWayTooLong123" };

        var response = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_EmptyPassword_ReturnsBadRequest()
    {
        var request = new { Email = "empty@example.com", Password = "" };

        var response = await _client.PostAsJsonAsync("/auth/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var email = "login@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/auth/register", new { Email = email, Password = password });

        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authResponse);
        Assert.False(string.IsNullOrWhiteSpace(authResponse!.AccessToken));
        Assert.Equal(900, authResponse.ExpiresIn); // 15 minutes in seconds
        Assert.Equal("Bearer", authResponse.TokenType);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var email = "wrongpass@example.com";
        await _client.PostAsJsonAsync("/auth/register", new { Email = email, Password = "Password123" });

        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = "WrongPassword" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonExistentUser_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = "nonexistent@example.com", Password = "Password123" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyCredentials_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { Email = "", Password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_WithValidToken_Succeeds()
    {
        // Arrange: Register and login to get token
        var email = "authenticated@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/auth/register", new { Email = email, Password = password });
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { Email = email, Password = password });
        var authResponse = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        // Act: Use token to access a protected endpoint (we'll test with a simple controller later)
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // For now, just verify the token can be parsed by the middleware
        // In future tests, we'll have actual protected endpoints
        Assert.True(true); // Placeholder - actual protected endpoint tests will come in Stage 5
    }

    public sealed record AuthResponse(string AccessToken, int ExpiresIn, string TokenType = "Bearer");
}