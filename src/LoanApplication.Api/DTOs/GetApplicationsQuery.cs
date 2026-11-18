using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace LoanApplication.Api.DTOs;

public sealed record GetApplicationsQuery(
    [FromQuery, Range(1, 1000)] int Page = 1,
    [FromQuery, Range(1, 100)] int PageSize = 20,
    [FromQuery] LoanApplication.Api.Entities.LoanStatus? Status = null
);