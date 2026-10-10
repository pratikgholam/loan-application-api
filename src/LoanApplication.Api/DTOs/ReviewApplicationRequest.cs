using System.ComponentModel.DataAnnotations;

namespace LoanApplication.Api.DTOs;

public sealed record ReviewApplicationRequest(
    [Required] bool? Approve,
    [StringLength(1000)] string? Reason
);