using LoanApplication.Api.Services;

namespace LoanApplication.Tests;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new PasswordHasher();

    [Fact]
    public void HashPassword_ReturnsNonEmptyString()
    {
        var hash = _hasher.HashPassword("Password123");
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var password = "Password123";
        var hash = _hasher.HashPassword(password);

        var result = _hasher.VerifyPassword(hash, password);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.HashPassword("Password123");

        var result = _hasher.VerifyPassword(hash, "WrongPassword");

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_ProducesDifferentHashes()
    {
        var password = "Password123";
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_DifferentHashesForSamePassword_BothVerify()
    {
        var password = "Password123";
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        Assert.True(_hasher.VerifyPassword(hash1, password));
        Assert.True(_hasher.VerifyPassword(hash2, password));
    }

    [Fact]
    public void HashPassword_EmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(""));
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword("   "));
    }

    [Fact]
    public void VerifyPassword_EmptyHash_Throws()
    {
        Assert.Throws<ArgumentException>(() => _hasher.VerifyPassword("", "Password123"));
        Assert.Throws<ArgumentException>(() => _hasher.VerifyPassword("   ", "Password123"));
    }

    [Fact]
    public void VerifyPassword_EmptyProvidedPassword_Throws()
    {
        var hash = _hasher.HashPassword("Password123");
        Assert.Throws<ArgumentException>(() => _hasher.VerifyPassword(hash, ""));
        Assert.Throws<ArgumentException>(() => _hasher.VerifyPassword(hash, "   "));
    }
}