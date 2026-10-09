using LoanApplication.Api.Entities;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace LoanApplication.Api.Common;

public interface ICurrentUserService
{
    Guid GetUserId();
    UserRole GetRole();
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
}