namespace LoanApplication.Api.Entities;

public class StatusHistory
{
    private StatusHistory() { } // required by EF Core
 
    internal StatusHistory(
        Guid loanId,
        LoanStatus? fromStatus,
        LoanStatus toStatus,
        Guid changedByUserId,
        DateTime changedAtUtc,
        string? note)
    {
        Id = Guid.NewGuid();
        LoanId = loanId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedByUserId = changedByUserId;
        ChangedAt = changedAtUtc;
        Note = note;
    }
 
    public Guid Id { get; private set; }
 
    public Guid LoanId { get; private set; }
    public Loan Loan { get; private set; } = null!;
 
    public LoanStatus? FromStatus { get; private set; } // null for the first entry
    public LoanStatus ToStatus { get; private set; }
 
    public Guid ChangedByUserId { get; private set; }
 
    public DateTime ChangedAt { get; private set; } // UTC
    public string? Note { get; private set; }

}