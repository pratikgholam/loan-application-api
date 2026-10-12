using LoanApplication.Api.Entities;
using System.Security.Claims;

namespace LoanApplication.Api.Services;

public interface ITokenService
{
    string GenerateToken(Guid userId, string email, UserRole role);
    ClaimsPrincipal? ValidateToken(string token);
}