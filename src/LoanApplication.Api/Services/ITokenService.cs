using LoanApplication.Api.Entities;

namespace LoanApplication.Api.Services;

public interface ITokenService
{
    string GenerateToken(Guid userId, string email, UserRole role);
}