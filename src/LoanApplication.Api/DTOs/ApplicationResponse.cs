using LoanApplication.Api.Entities;

namespace LoanApplication.Api.DTOs;

public sealed record ApplicationResponse(
    Guid Id,
    Guid ApplicantId,
    decimal Amount,
    int TermMonths,
    string Purpose,
    LoanType LoanType,
    LoanStatus Status,
    Guid? AssignedOfficerUserId,
    DateTime CreatedAt,
    IReadOnlyCollection<StatusHistoryDto> History
);
