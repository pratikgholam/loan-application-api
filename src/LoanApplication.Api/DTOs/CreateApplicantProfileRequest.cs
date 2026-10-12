using System.ComponentModel.DataAnnotations;

namespace LoanApplication.Api.DTOs;

public sealed record CreateApplicantProfileRequest(
    [Required, StringLength(200, MinimumLength = 1)] string FullName,
    [Required] DateOnly DateOfBirth,
    [Required, Range(typeof(decimal), "0", "10000000")] decimal MonthlyIncome
);