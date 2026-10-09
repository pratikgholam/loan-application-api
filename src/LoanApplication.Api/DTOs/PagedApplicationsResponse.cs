namespace LoanApplication.Api.DTOs;

public sealed record PagedApplicationsResponse(
    IReadOnlyCollection<ApplicationSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages
);