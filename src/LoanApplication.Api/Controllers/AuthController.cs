using LoanApplication.Api.Data;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LoanApplication.Api.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthController(AppDbContext db, IPasswordHasher passwordHasher, ITokenService tokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public sealed record RegisterRequest(string Email, string Password);

    public sealed record LoginRequest(string Email, string Password);

    public sealed record AuthResponse(string AccessToken, int ExpiresIn, string TokenType = "Bearer");

    [HttpPost("register")]
    public async Task<ActionResult<Guid>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!IsValidEmail(request.Email))
            return BadRequest(new { error = "Invalid email format." });

        if (!IsValidPassword(request.Password))
            return BadRequest(new { error = "Password must be 8-12 characters." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var exists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (exists)
            return Conflict(new { error = "Email already registered." });

        var passwordHash = _passwordHasher.HashPassword(request.Password);
        var user = new User(normalizedEmail, passwordHash, UserRole.Applicant);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Register), new { id = user.Id }, user.Id);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email and password are required." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is null || !_passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
            return Unauthorized(new { error = "Invalid credentials." });

        var token = _tokenService.GenerateToken(user.Id, user.Email, user.Role);
        var expiresIn = 15 * 60; // 15 minutes in seconds, matches JwtOptions.AccessTokenMinutes

        return Ok(new AuthResponse(token, expiresIn));
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email.Trim();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;
        var len = password.Length;
        return len >= 8 && len <= 12;
    }
}