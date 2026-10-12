using LoanApplication.Api.Entities;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace LoanApplication.Api.Common;

public interface ICurrentUserService
{
    Guid GetUserId();
    UserRole GetRole();
    bool IsOfficer();
}

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("User ID claim not found.");
        return Guid.Parse(userIdClaim.Value);
    }

    public UserRole GetRole()
    {
        var roleClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role)
            ?? throw new UnauthorizedAccessException("Role claim not found.");
        return Enum.Parse<UserRole>(roleClaim.Value);
    }

    public bool IsOfficer()
    {
        var roleClaim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role);
        if (roleClaim is null)
            return false;
        return Enum.TryParse<UserRole>(roleClaim.Value, out var role) &&
               role is UserRole.LoanOfficer or UserRole.Admin;
    }
}