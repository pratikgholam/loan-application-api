using Dapper;
using LoanApplication.Api.DTOs;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LoanApplication.Api.Services;

public sealed class ReportService : IReportService
{
    private readonly string _connectionString;

    public ReportService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
    }

    public async Task<IReadOnlyList<StatusSummaryDto>> GetStatusSummaryAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT "Status" AS Status, COUNT(*) AS Count, SUM("Amount") AS TotalAmount
            FROM "Loans"
            GROUP BY "Status"
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var rows = await connection.QueryAsync<StatusSummaryDto>(sql);
        return rows.ToList().AsReadOnly();
    }
}