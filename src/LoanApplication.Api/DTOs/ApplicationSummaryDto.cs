namespace LoanApplication.Api.DTOs;

public sealed record ApplicationSummaryDto(
    Guid Id,
    Guid ApplicantId,
    decimal Amount,
    int TermMonths,
    string Purpose,
    LoanApplication.Api.Entities.LoanStatus Status,
    DateTime CreatedAt
);