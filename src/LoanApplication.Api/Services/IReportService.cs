using LoanApplication.Api.DTOs;

namespace LoanApplication.Api.Services;

public interface IReportService
{
    Task<IReadOnlyList<StatusSummaryDto>> GetStatusSummaryAsync(CancellationToken ct);
}