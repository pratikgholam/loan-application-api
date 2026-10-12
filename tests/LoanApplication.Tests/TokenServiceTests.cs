using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LoanApplication.Tests;

public class TokenServiceTests
{
    private readonly ITokenService _tokenService;

    public TokenServiceTests()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Key = "dGVzdF9zZWNyZXRfa2V5X2Zvcl91bml0X3Rlc3RzXzMyX2J5dGVzXzEyMzQ1Njc4OTA=",
            AccessTokenMinutes = 15
        });
        _tokenService = new TokenService(options);
    }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var token = _tokenService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateToken_ContainsCorrectClaims()
    {
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var role = UserRole.LoanOfficer;

        var tokenString = _tokenService.GenerateToken(userId, email, role);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        Assert.Equal(userId.ToString(), token.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(email, token.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(role.ToString(), token.Claims.First(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal("test-issuer", token.Issuer);
        Assert.Contains("test-audience", token.Audiences);
        Assert.NotNull(token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti));
        Assert.NotNull(token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iat));
    }

    [Fact]
    public void GenerateToken_ExpirationIsCorrect()
    {
        var before = DateTime.UtcNow;
        var tokenString = _tokenService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);
        var after = DateTime.UtcNow;

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(tokenString);

        Assert.True(token.ValidTo > before.AddMinutes(14));
        Assert.True(token.ValidTo < after.AddMinutes(16));
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        var userId = Guid.NewGuid();
        var tokenString = _tokenService.GenerateToken(userId, "test@example.com", UserRole.Admin);

        var principal = _tokenService.ValidateToken(tokenString);

        Assert.NotNull(principal);
        // After validation, the sub claim maps to ClaimTypes.NameIdentifier
        var subClaim = principal!.FindFirst(ClaimTypes.NameIdentifier) ?? principal.FindFirst(JwtRegisteredClaimNames.Sub);
        Assert.Equal(userId.ToString(), subClaim?.Value);
        Assert.Equal("Admin", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("dGVzdF9zZWNyZXRfa2V5X2Zvcl91bml0X3Rlc3RzXzMyX2J5dGVzXzEyMzQ1Njc4OTA="));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Create a token that expired 5 minutes ago
        var expiredToken = new JwtSecurityToken(
            issuer: "test-issuer",
            audience: "test-audience",
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "test@example.com"),
                new Claim(ClaimTypes.Role, "Applicant"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            },
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5), // Expired 5 minutes ago
            signingCredentials: creds
        );
        var expiredTokenString = handler.WriteToken(expiredToken);

        var principal = _tokenService.ValidateToken(expiredTokenString);

        Assert.Null(principal);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var tokenString = _tokenService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);
        var tampered = tokenString[..^5] + "abcde"; // Change last 5 chars

        var principal = _tokenService.ValidateToken(tampered);

        Assert.Null(principal);
    }

    [Fact]
    public void ValidateToken_WrongIssuer_ReturnsNull()
    {
        var wrongIssuerOptions = Options.Create(new JwtOptions
        {
            Issuer = "wrong-issuer",
            Audience = "test-audience",
            Key = "dGVzdF9zZWNyZXRfa2V5X2Zvcl91bml0X3Rlc3RzXzMyX2J5dGVzXzEyMzQ1Njc4OTA=",
            AccessTokenMinutes = 15
        });
        var wrongIssuerService = new TokenService(wrongIssuerOptions);
        var tokenString = wrongIssuerService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);

        var principal = _tokenService.ValidateToken(tokenString);

        Assert.Null(principal);
    }

    [Fact]
    public void ValidateToken_WrongAudience_ReturnsNull()
    {
        var wrongAudienceOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "wrong-audience",
            Key = "dGVzdF9zZWNyZXRfa2V5X2Zvcl91bml0X3Rlc3RzXzMyX2J5dGVzXzEyMzQ1Njc4OTA=",
            AccessTokenMinutes = 15
        });
        var wrongAudienceService = new TokenService(wrongAudienceOptions);
        var tokenString = wrongAudienceService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);

        var principal = _tokenService.ValidateToken(tokenString);

        Assert.Null(principal);
    }

    [Fact]
    public void ValidateToken_WrongKey_ReturnsNull()
    {
        var wrongKeyOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Key = "d3Jvbmdfc2VjcmV0X2tleV9mb3JfdW5pdF90ZXN0c18zMl9ieXRlc18xMjM0NTY3ODkw",
            AccessTokenMinutes = 15
        });
        var wrongKeyService = new TokenService(wrongKeyOptions);
        var tokenString = wrongKeyService.GenerateToken(Guid.NewGuid(), "test@example.com", UserRole.Applicant);

        var principal = _tokenService.ValidateToken(tokenString);

        Assert.Null(principal);
    }
}