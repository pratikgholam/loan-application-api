using LoanApplication.Api.DTOs;
using LoanApplication.Api.Entities;

namespace LoanApplication.Api.Services;

public interface IApplicationService
{
    Task<ApplicationResponse> CreateAsync(Guid applicantUserId, CreateApplicationRequest request, DateTime nowUtc, CancellationToken ct);
    Task<ApplicationResponse?> GetOwnAsync(Guid applicantUserId, Guid loanId, CancellationToken ct);
    Task<ApplicationResponse?> GetByIdForOfficerAsync(Guid loanId, CancellationToken ct);
    Task<PagedApplicationsResponse> GetPagedAsync(GetApplicationsQuery query, CancellationToken ct);
    Task<ApplicationResponse> StartReviewAsync(Guid officerUserId, Guid loanId, DateTime nowUtc, CancellationToken ct);
    Task<ApplicationResponse> ReviewAsync(Guid officerUserId, Guid loanId, ReviewApplicationRequest request, DateTime nowUtc, CancellationToken ct);
    Task<ApplicationResponse?> WithdrawAsync(Guid applicantUserId, Guid loanId, DateTime nowUtc, CancellationToken ct);
    Task<ApplicantProfileResponse> CreateApplicantProfileAsync(Guid userId, CreateApplicantProfileRequest request, CancellationToken ct);
}