using LoanApplication.Api.Data;
using LoanApplication.Api.DTOs;
using LoanApplication.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoanApplication.Api.Services;

public sealed class ApplicationService : IApplicationService
{
    private readonly AppDbContext _db;

    public ApplicationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResponse> CreateAsync(Guid applicantUserId, CreateApplicationRequest request, DateTime nowUtc, CancellationToken ct)
    {
        // Find or create applicant profile for this user
        var applicant = await _db.Applicants
            .FirstOrDefaultAsync(a => a.UserId == applicantUserId, ct);

        if (applicant is null)
        {
            // Normally a registered applicant has a profile already, but the client may
            // call POST /applications before POST /applications/profile.
            throw new InvalidOperationException("Applicant profile not found. Please create your applicant profile first.");
        }

        var loan = Loan.Submit(
            applicant.Id,
            applicantUserId,
            request.Amount,
            request.TermMonths,
            request.Purpose,
            nowUtc);

        _db.Loans.Add(loan);
        await _db.SaveChangesAsync(ct);

        return MapToResponse(loan);
    }

    // Applicants can only see their own loans; a non-owned id reads as 404 for security.
    public async Task<ApplicationResponse?> GetOwnAsync(Guid applicantUserId, Guid loanId, CancellationToken ct)
    {
        var loan = await _db.Loans
            .Include(l => l.Applicant)
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct);

        if (loan is null)
            return null;

        if (loan.Applicant.UserId != applicantUserId)
            return null; // Treat as not found for security

        return MapToResponse(loan);
    }

    // Officers and admins may read any application; no ownership check applies.
    public async Task<ApplicationResponse?> GetByIdForOfficerAsync(Guid loanId, CancellationToken ct)
    {
        var loan = await _db.Loans
            .Include(l => l.Applicant)
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct);

        if (loan is null)
            return null;

        return MapToResponse(loan);
    }

    public async Task<PagedApplicationsResponse> GetPagedAsync(GetApplicationsQuery query, CancellationToken ct)
    {
        // No Include needed: the summary mapping uses only scalar Loan properties.
        var queryable = _db.Loans.AsQueryable();

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(l => l.Status == query.Status.Value);
        }

        var totalCount = await queryable.CountAsync(ct);

        var loans = await queryable
            .OrderByDescending(l => l.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = loans.Select(MapToSummary).ToList();

        return new PagedApplicationsResponse(
            Items: items,
            Page: query.Page,
            PageSize: query.PageSize,
            TotalCount: totalCount,
            TotalPages: (int)Math.Ceiling(totalCount / (double)query.PageSize)
        );
    }

    public async Task<ApplicationResponse> StartReviewAsync(Guid officerUserId, Guid loanId, DateTime nowUtc, CancellationToken ct)
    {
        var loan = await _db.Loans
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct);

        if (loan is null)
            throw new KeyNotFoundException("Loan not found.");

        loan.StartReview(officerUserId, nowUtc);
        await _db.SaveChangesAsync(ct);

        return MapToResponse(loan);
    }

    public async Task<ApplicationResponse> ReviewAsync(Guid officerUserId, Guid loanId, ReviewApplicationRequest request, DateTime nowUtc, CancellationToken ct)
    {
        var loan = await _db.Loans
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct);

        if (loan is null)
            throw new KeyNotFoundException("Loan not found.");

        // Defense-in-depth: [ApiController] model validation rejects a missing Approve
        // before the action runs, so this only triggers for direct service callers.
        if (request.Approve is null)
            throw new ArgumentException("An approve decision (approve or reject) must be specified.", nameof(request.Approve));

        if (request.Approve.Value)
        {
            loan.Approve(officerUserId, nowUtc, request.Reason?.Trim());
        }
        else
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Reason is required when rejecting an application.", nameof(request.Reason));

            loan.Reject(officerUserId, nowUtc, reason);
        }

        await _db.SaveChangesAsync(ct);
        return MapToResponse(loan);
    }

    public async Task<ApplicationResponse?> WithdrawAsync(Guid applicantUserId, Guid loanId, DateTime nowUtc, CancellationToken ct)
    {
        var loan = await _db.Loans
            .Include(l => l.Applicant)
            .Include(l => l.History)
            .FirstOrDefaultAsync(l => l.Id == loanId, ct);

        if (loan is null)
            throw new KeyNotFoundException("Loan not found.");

        // Ownership check: applicants can only withdraw their own loans.
        if (loan.Applicant.UserId != applicantUserId)
            return null; // Treat as not found for security (same as GetOwnAsync)

        loan.Withdraw(applicantUserId, nowUtc);
        await _db.SaveChangesAsync(ct);

        return MapToResponse(loan);
    }

    private static ApplicationResponse MapToResponse(Loan loan)
        => new ApplicationResponse(
            loan.Id,
            loan.ApplicantId,
            loan.Amount,
            loan.TermMonths,
            loan.Purpose,
            loan.Status,
            loan.CreatedAt,
            loan.History.Select(h => new StatusHistoryDto(h.FromStatus, h.ToStatus, h.ChangedByUserId, h.ChangedAt, h.Note)).ToList()
        );

    private static ApplicationSummaryDto MapToSummary(Loan loan)
        => new ApplicationSummaryDto(
            loan.Id,
            loan.ApplicantId,
            loan.Amount,
            loan.TermMonths,
            loan.Purpose,
            loan.Status,
            loan.CreatedAt
        );

    public async Task<ApplicantProfileResponse> CreateApplicantProfileAsync(Guid userId, CreateApplicantProfileRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            throw new KeyNotFoundException("User not found.");

        if (user.Role != UserRole.Applicant)
            throw new InvalidOperationException("Only applicants can create an applicant profile.");

        var existing = await _db.Applicants.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        if (existing is not null)
            throw new InvalidOperationException("Applicant profile already exists.");

        var applicant = new Applicant(userId, request.FullName, request.DateOfBirth, request.MonthlyIncome);
        _db.Applicants.Add(applicant);
        await _db.SaveChangesAsync(ct);

        return new ApplicantProfileResponse(
            applicant.Id, applicant.UserId, applicant.FullName, applicant.DateOfBirth, applicant.MonthlyIncome);
    }
}