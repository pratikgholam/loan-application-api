using LoanApplication.Api.Data;
using LoanApplication.Api.Entities;
using LoanApplication.Api.Messaging;
using Microsoft.EntityFrameworkCore;

namespace LoanApplication.Worker;

/// <summary>Assigns a submitted loan to the officer whose expertise matches its type.</summary>
public sealed class LoanAssignmentHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<LoanAssignmentHandler> _logger;

    public LoanAssignmentHandler(AppDbContext db, ILogger<LoanAssignmentHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns normally when the message is safe to acknowledge. Throwing leaves the
    /// message unacknowledged so it is redelivered.
    /// </summary>
    public async Task HandleAsync(LoanSubmitted message, CancellationToken ct)
    {
        var loan = await _db.Loans.FirstOrDefaultAsync(l => l.Id == message.LoanId, ct);
        if (loan is null)
        {
            _logger.LogWarning("Loan {LoanId} from LoanSubmitted was not found; acknowledging.", message.LoanId);
            return;
        }

        // Redelivery after a successful save is a no-op.
        if (loan.AssignedOfficerUserId is not null)
            return;

        var officerId = await _db.Users
            .Where(u => u.Role == UserRole.LoanOfficer && u.Expertise == message.LoanType)
            .OrderBy(u => u.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (officerId is null)
        {
            _logger.LogWarning(
                "No loan officer has expertise {LoanType}; loan {LoanId} stays unassigned.",
                message.LoanType, message.LoanId);
            return;
        }

        loan.AssignOfficer(officerId.Value);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Assigned loan {LoanId} ({LoanType}) to officer {OfficerId}.",
            message.LoanId, message.LoanType, officerId.Value);
    }
}
