using LoanApplication.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LoanApplication.Tests;

/// <summary>
/// Pure unit tests for <see cref="ReportService"/> that need no database.
/// </summary>
public class ReportServiceTests
{
    [Fact]
    public void Constructor_MissingConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange: configuration without ConnectionStrings:Default
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { })
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new ReportService(config));
        Assert.Equal("Connection string 'Default' is not configured.", ex.Message);
    }

    [Fact]
    public void Constructor_NullConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange: configuration with explicit null
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = null })
            .Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new ReportService(config));
        Assert.Equal("Connection string 'Default' is not configured.", ex.Message);
    }

    [Fact]
    public void Constructor_ValidConnectionString_DoesNotThrow()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=test"
            })
            .Build();

        // Act & Assert
        var service = new ReportService(config);
        Assert.NotNull(service);
    }
}