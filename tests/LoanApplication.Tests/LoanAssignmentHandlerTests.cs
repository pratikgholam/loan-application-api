using LoanApplication.Api.Data;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Messaging;
using LoanApplication.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoanApplication.Tests;

/// <summary>
/// Worker match logic without a broker or a real database.
/// </summary>
public class LoanAssignmentHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Loan NewLoan(LoanType type) =>
        Loan.Submit(Guid.NewGuid(), Guid.NewGuid(), 10_000m, 12, "Test", type, Now);

    private static LoanAssignmentHandler NewHandler(AppDbContext db) =>
        new(db, NullLogger<LoanAssignmentHandler>.Instance);

    [Fact]
    public async Task Handle_MatchingExpertise_AssignsThatOfficer()
    {
        await using var db = NewDb();
        var officer = new User("auto@example.com", "hash", UserRole.LoanOfficer, LoanType.Auto);
        var loan = NewLoan(LoanType.Auto);
        db.Users.Add(officer);
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(new LoanSubmitted(loan.Id, LoanType.Auto), CancellationToken.None);

        Assert.Equal(officer.Id, (await db.Loans.SingleAsync()).AssignedOfficerUserId);
    }

    [Fact]
    public async Task Handle_NoMatchingExpertise_LeavesLoanUnassigned()
    {
        await using var db = NewDb();
        // An officer exists, but for a different loan type.
        db.Users.Add(new User("auto@example.com", "hash", UserRole.LoanOfficer, LoanType.Auto));
        var loan = NewLoan(LoanType.Home);
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(new LoanSubmitted(loan.Id, LoanType.Home), CancellationToken.None);

        Assert.Null((await db.Loans.SingleAsync()).AssignedOfficerUserId);
    }

    [Fact]
    public async Task Handle_AlreadyAssigned_KeepsOriginalOfficer()
    {
        await using var db = NewDb();
        var loan = NewLoan(LoanType.Auto);
        var originalOfficer = Guid.NewGuid();
        loan.AssignOfficer(originalOfficer);
        db.Users.Add(new User("auto@example.com", "hash", UserRole.LoanOfficer, LoanType.Auto));
        db.Loans.Add(loan);
        await db.SaveChangesAsync();

        await NewHandler(db).HandleAsync(new LoanSubmitted(loan.Id, LoanType.Auto), CancellationToken.None);

        Assert.Equal(originalOfficer, (await db.Loans.SingleAsync()).AssignedOfficerUserId);
    }

    [Fact]
    public async Task Handle_UnknownLoan_DoesNotThrow()
    {
        await using var db = NewDb();

        await NewHandler(db).HandleAsync(new LoanSubmitted(Guid.NewGuid(), LoanType.Auto), CancellationToken.None);
    }
}
