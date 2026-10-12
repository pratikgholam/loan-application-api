using LoanApplication.Api.DTOs;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApplication.Api.Controllers;

[ApiController]
[Route("reports")]
[Authorize(Policy = "LoanOfficerOrAdmin")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet("status-summary")]
    public async Task<ActionResult<IReadOnlyList<StatusSummaryDto>>> GetStatusSummary(CancellationToken ct)
        => Ok(await _reports.GetStatusSummaryAsync(ct));
}