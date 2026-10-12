namespace LoanApplication.Api.Entities;

public class Loan
{
    private readonly List<StatusHistory> _history = new();

    private Loan()
    {
    } // required by EF Core

    public static Loan Submit(
        Guid applicantId,
        Guid submittedByUserId,
        decimal amount,
        int termMonths,
        string purpose,
        DateTime nowUtc)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        if (termMonths <= 0)
            throw new ArgumentOutOfRangeException(nameof(termMonths), "Term must be greater than zero.");
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose is required.", nameof(purpose));
        RequireUtc(nowUtc, nameof(nowUtc));

        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            ApplicantId = applicantId,
            Amount = amount,
            TermMonths = termMonths,
            Purpose = purpose.Trim(),
            Status = LoanStatus.Submitted,
            CreatedAt = nowUtc
        };

        loan._history.Add(new StatusHistory(
            loan.Id, fromStatus: null, LoanStatus.Submitted, submittedByUserId, nowUtc, note: null));

        return loan;
    }

    public Guid Id { get; private set; }

    public Guid ApplicantId { get; private set; }
    public Applicant Applicant { get; private set; } = null!;

    public decimal Amount { get; private set; }
    public int TermMonths { get; private set; }
    public string Purpose { get; private set; } = null!;
    public LoanStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; } // UTC

    //public IReadOnlyCollection<StatusHistory> History => _history;
    public IReadOnlyCollection<StatusHistory> History => _history.AsReadOnly(); 

    public void StartReview(Guid officerUserId, DateTime nowUtc)
        => ChangeStatus(LoanStatus.UnderReview, officerUserId, nowUtc, note: null);

    public void Approve(Guid officerUserId, DateTime nowUtc, string? note = null)
        => ChangeStatus(LoanStatus.Approved, officerUserId, nowUtc, note);

    public void Reject(Guid officerUserId, DateTime nowUtc, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required to reject an application.", nameof(reason));

        ChangeStatus(LoanStatus.Rejected, officerUserId, nowUtc, reason);
    }

    public void Withdraw(Guid applicantUserId, DateTime nowUtc)
        => ChangeStatus(LoanStatus.Withdrawn, applicantUserId, nowUtc, note: null);

    private void ChangeStatus(LoanStatus to, Guid changedByUserId, DateTime nowUtc, string? note)
    {
        RequireUtc(nowUtc, nameof(nowUtc));
        if (!IsAllowed(Status, to))
            throw new InvalidOperationException($"Cannot change status from {Status} to {to}.");

        _history.Add(new StatusHistory(Id, Status, to, changedByUserId, nowUtc, note));
        Status = to;
    }

    private static void RequireUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be UTC.", paramName);
    }

    private static bool IsAllowed(LoanStatus from, LoanStatus to) => (from, to) switch
    {
        (LoanStatus.Submitted, LoanStatus.UnderReview) => true,
        (LoanStatus.UnderReview, LoanStatus.Approved) => true,
        (LoanStatus.UnderReview, LoanStatus.Rejected) => true,
        (LoanStatus.Submitted or LoanStatus.UnderReview, LoanStatus.Withdrawn) => true,
        _ => false
    };
}