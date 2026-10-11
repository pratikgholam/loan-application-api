namespace LoanApplication.Api.DTOs;

/// <summary>
/// Count is <see cref="long"/> because PostgreSQL returns COUNT(*) as bigint and Dapper's
/// constructor matching requires an exact type match. A mismatch throws at materialization.
/// </summary>
public sealed record StatusSummaryDto(string Status, long Count, decimal TotalAmount);