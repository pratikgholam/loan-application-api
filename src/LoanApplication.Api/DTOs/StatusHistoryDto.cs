namespace LoanApplication.Api.DTOs;

public sealed record StatusHistoryDto(
    LoanApplication.Api.Entities.LoanStatus? FromStatus,
    LoanApplication.Api.Entities.LoanStatus ToStatus,
    Guid ChangedByUserId,
    DateTime ChangedAt,
    string? Note
);