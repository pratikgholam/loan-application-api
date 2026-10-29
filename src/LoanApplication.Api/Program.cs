using LoanApplication.Api.Common;
using LoanApplication.Api.Data;
using LoanApplication.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var useInMemory = builder.Configuration.GetValue<bool>("UseInMemoryDatabase");

if (!useInMemory)
{
    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(connectionString));
}
// When UseInMemoryDatabase=true, the test factory will register the DbContext

// JWT configuration
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IApplicationService, ApplicationService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
        if (Encoding.UTF8.GetBytes(jwtOptions.Key).Length < 32)
            throw new InvalidOperationException("JWT signing key must be at least 32 bytes for HS256.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApplicantOrOfficer", p => p.RequireRole("Applicant", "LoanOfficer", "Admin"));
    options.AddPolicy("ApplicantOnly", p => p.RequireRole("Applicant"));
    options.AddPolicy("LoanOfficerOrAdmin", p => p.RequireRole("LoanOfficer", "Admin"));
});

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Apply pending migrations on startup so a fresh database (e.g. from
// `docker compose up`) is usable without an out-of-band `dotnet ef database update`.
// Skipped when UseInMemoryDatabase is set; the test factory registers its own InMemory context.
if (!useInMemory)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "Loan Application API";
        options.Theme = ScalarTheme.Moon;
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();