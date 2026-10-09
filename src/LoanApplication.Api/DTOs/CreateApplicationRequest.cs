using System.ComponentModel.DataAnnotations;

namespace LoanApplication.Api.DTOs;

public sealed record CreateApplicationRequest(
    [Required, Range(typeof(decimal), "0.01", "10000000")] decimal Amount,
    [Required, Range(1, 360)] int TermMonths,
    [Required, StringLength(500, MinimumLength = 1)] string Purpose
);