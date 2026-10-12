using LoanApplication.Api.Common;
using LoanApplication.Api.DTOs;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LoanApplication.Api.Controllers;

[ApiController]
[Route("applications")]
[Authorize] // All endpoints require authentication
public sealed class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;
    private readonly ICurrentUserService _currentUser;

    public ApplicationsController(IApplicationService applicationService, ICurrentUserService currentUser)
    {
        _applicationService = applicationService;
        _currentUser = currentUser;
    }

    // POST /applications/profile - Applicant creates their profile (required before applying)
    [HttpPost("profile")]
    [Authorize(Policy = "ApplicantOnly")]
    public async Task<ActionResult<ApplicantProfileResponse>> CreateProfile(
        [FromBody] CreateApplicantProfileRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserId();
        try
        {
            var applicant = await _applicationService.CreateApplicantProfileAsync(userId, request, ct);
            // Profile records are always owned by the authenticated user, so there is no fetchable
            // profile URL; a 200 response with the created record is returned instead of 201.
            return Ok(new ApplicantProfileResponse(
                applicant.Id, applicant.UserId, applicant.FullName, applicant.DateOfBirth, applicant.MonthlyIncome));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "User not found." });
        }
    }

    // POST /applications - Applicant creates a new loan application
    [HttpPost]
    [Authorize(Policy = "ApplicantOnly")]
    public async Task<ActionResult<ApplicationResponse>> Create(
        [FromBody] CreateApplicationRequest request,
        CancellationToken ct)
    {
        var userId = _currentUser.GetUserId();
        try
        {
            var loan = await _applicationService.CreateAsync(userId, request, DateTime.UtcNow, ct);
            return CreatedAtAction(nameof(GetById), new { id = loan.Id }, loan);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // GET /applications/{id} - Applicant views their own application, LoanOfficer views any
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "ApplicantOrOfficer")]
    public async Task<ActionResult<ApplicationResponse>> GetById(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId();
        // Officers read any application; applicants are restricted to their own loans
        // by GetOwnAsync, so the wrong method cannot be called by accident.
        var loan = _currentUser.IsOfficer()
            ? await _applicationService.GetByIdForOfficerAsync(id, ct)
            : await _applicationService.GetOwnAsync(userId, id, ct);

        if (loan is null)
            return NotFound(new { error = "Application not found." });

        return Ok(loan);
    }

    // GET /applications - LoanOfficer/Admin lists applications with pagination and optional status filter
    [HttpGet]
    [Authorize(Policy = "LoanOfficerOrAdmin")]
    public async Task<ActionResult<PagedApplicationsResponse>> GetAll(
        [FromQuery] GetApplicationsQuery query,
        CancellationToken ct)
    {
        var result = await _applicationService.GetPagedAsync(query, ct);
        return Ok(result);
    }

    // POST /applications/{id}/start-review - LoanOfficer/Admin starts reviewing an application
    [HttpPost("{id:guid}/start-review")]
    [Authorize(Policy = "LoanOfficerOrAdmin")]
    public async Task<ActionResult<ApplicationResponse>> StartReview(Guid id, CancellationToken ct)
    {
        var officerId = _currentUser.GetUserId();
        try
        {
            var loan = await _applicationService.StartReviewAsync(officerId, id, DateTime.UtcNow, ct);
            return Ok(loan);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Application not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // POST /applications/{id}/review - LoanOfficer/Admin approves or rejects an application
    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = "LoanOfficerOrAdmin")]
    public async Task<ActionResult<ApplicationResponse>> Review(
        Guid id,
        [FromBody] ReviewApplicationRequest request,
        CancellationToken ct)
    {
        var officerId = _currentUser.GetUserId();
        try
        {
            var loan = await _applicationService.ReviewAsync(officerId, id, request, DateTime.UtcNow, ct);
            return Ok(loan);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Application not found." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // POST /applications/{id}/withdraw - Applicant withdraws their own application
    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Policy = "ApplicantOnly")]
    public async Task<ActionResult<ApplicationResponse>> Withdraw(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.GetUserId();
        try
        {
            var loan = await _applicationService.WithdrawAsync(userId, id, DateTime.UtcNow, ct);

            if (loan is null)
                return NotFound(new { error = "Application not found." });

            return Ok(loan);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}