namespace LoanApplication.Api.DTOs;

/// <summary>
/// Shallow representation of an applicant profile returned by <c>POST /applications/profile</c>.
/// Returned as a record rather than the entity so the domain model stays out of the API contract.
/// </summary>
public sealed record ApplicantProfileResponse(
    Guid Id,
    Guid UserId,
    string FullName,
    DateOnly DateOfBirth,
    decimal MonthlyIncome
);