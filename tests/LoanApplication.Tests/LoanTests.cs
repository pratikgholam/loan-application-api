using LoanApplication.Api.Entities;

namespace LoanApplication.Tests;

public class LoanTests
{
    public enum Step { StartReview, Approve, Reject, Withdraw }

    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ApplicantId = Guid.NewGuid();
    private static readonly Guid ApplicantUserId = Guid.NewGuid();
    private static readonly Guid OfficerId = Guid.NewGuid();

    private static readonly (LoanStatus From, Step Step)[] AllowedTransitions =
    {
        (LoanStatus.Submitted, Step.StartReview),
        (LoanStatus.UnderReview, Step.Approve),
        (LoanStatus.UnderReview, Step.Reject),
        (LoanStatus.Submitted, Step.Withdraw),
        (LoanStatus.UnderReview, Step.Withdraw),
    };

    public static TheoryData<LoanStatus, Step> ValidTransitions
    {
        get
        {
            var data = new TheoryData<LoanStatus, Step>();
            foreach (var (from, step) in AllowedTransitions) data.Add(from, step);
            return data;
        }
    }

    public static TheoryData<LoanStatus, Step> InvalidTransitions
    {
        get
        {
            var data = new TheoryData<LoanStatus, Step>();
            foreach (var from in Enum.GetValues<LoanStatus>())
            foreach (var step in Enum.GetValues<Step>())
                if (!AllowedTransitions.Contains((from, step))) data.Add(from, step);
            return data;
        }
    }

    // ---------- helpers ----------

    private static Loan NewLoan() =>
        Loan.Submit(ApplicantId, ApplicantUserId, 10_000m, 24, "Car", T0);

    private static Loan InState(LoanStatus status)
    {
        var loan = NewLoan();
        switch (status)
        {
            case LoanStatus.Submitted:
                break;
            case LoanStatus.UnderReview:
                loan.StartReview(OfficerId, T0.AddHours(1));
                break;
            case LoanStatus.Approved:
                loan.StartReview(OfficerId, T0.AddHours(1));
                loan.Approve(OfficerId, T0.AddHours(2));
                break;
            case LoanStatus.Rejected:
                loan.StartReview(OfficerId, T0.AddHours(1));
                loan.Reject(OfficerId, T0.AddHours(2), "Insufficient income");
                break;
            case LoanStatus.Withdrawn:
                loan.Withdraw(ApplicantUserId, T0.AddHours(1));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
        return loan;
    }

    private static Guid ActorFor(Step step) =>
        step == Step.Withdraw ? ApplicantUserId : OfficerId;

    private static LoanStatus TargetOf(Step step) => step switch
    {
        Step.StartReview => LoanStatus.UnderReview,
        Step.Approve => LoanStatus.Approved,
        Step.Reject => LoanStatus.Rejected,
        Step.Withdraw => LoanStatus.Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };

    private static void Apply(Loan loan, Step step, DateTime at)
    {
        switch (step)
        {
            case Step.StartReview: loan.StartReview(OfficerId, at); break;
            case Step.Approve: loan.Approve(OfficerId, at); break;
            case Step.Reject: loan.Reject(OfficerId, at, "Insufficient income"); break;
            case Step.Withdraw: loan.Withdraw(ApplicantUserId, at); break;
            default: throw new ArgumentOutOfRangeException(nameof(step));
        }
    }

    // ---------- Submit ----------

    [Fact]
    public void Submit_WithValidInput_CreatesSubmittedLoanWithInitialHistory()
    {
        var loan = NewLoan();

        Assert.NotEqual(Guid.Empty, loan.Id);
        Assert.Equal(ApplicantId, loan.ApplicantId);
        Assert.Equal(10_000m, loan.Amount);
        Assert.Equal(24, loan.TermMonths);
        Assert.Equal("Car", loan.Purpose);
        Assert.Equal(LoanStatus.Submitted, loan.Status);
        Assert.Equal(T0, loan.CreatedAt);

        var entry = Assert.Single(loan.History);
        Assert.Equal(loan.Id, entry.LoanId);
        Assert.Null(entry.FromStatus);
        Assert.Equal(LoanStatus.Submitted, entry.ToStatus);
        Assert.Equal(ApplicantUserId, entry.ChangedByUserId);
        Assert.Equal(T0, entry.ChangedAt);
        Assert.Null(entry.Note);
    }

    [Fact]
    public void Submit_TrimsPurpose()
    {
        var loan = Loan.Submit(ApplicantId, ApplicantUserId, 1m, 1, "  Car  ", T0);
        Assert.Equal("Car", loan.Purpose);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_WithNonPositiveAmount_Throws(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Loan.Submit(ApplicantId, ApplicantUserId, amount, 12, "Car", T0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_WithNonPositiveTerm_Throws(int term)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Loan.Submit(ApplicantId, ApplicantUserId, 1000m, term, "Car", T0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_WithBlankPurpose_Throws(string? purpose)
    {
        Assert.Throws<ArgumentException>(() =>
            Loan.Submit(ApplicantId, ApplicantUserId, 1000m, 12, purpose!, T0));
    }

    // ---------- Transitions ----------

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void ValidTransition_UpdatesStatusAndAppendsHistory(LoanStatus from, Step step)
    {
        var loan = InState(from);
        var countBefore = loan.History.Count;
        var at = T0.AddDays(1);

        Apply(loan, step, at);

        Assert.Equal(TargetOf(step), loan.Status);
        Assert.Equal(countBefore + 1, loan.History.Count);

        var last = loan.History.Last();
        Assert.Equal(loan.Id, last.LoanId);
        Assert.Equal(from, last.FromStatus);
        Assert.Equal(TargetOf(step), last.ToStatus);
        Assert.Equal(ActorFor(step), last.ChangedByUserId);
        Assert.Equal(at, last.ChangedAt);
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void InvalidTransition_ThrowsAndLeavesLoanUnchanged(LoanStatus from, Step step)
    {
        var loan = InState(from);
        var countBefore = loan.History.Count;

        Assert.Throws<InvalidOperationException>(() => Apply(loan, step, T0.AddDays(1)));

        Assert.Equal(from, loan.Status);
        Assert.Equal(countBefore, loan.History.Count);
    }

    [Fact]
    public void Approve_StoresOptionalNote()
    {
        var loan = InState(LoanStatus.UnderReview);
        loan.Approve(OfficerId, T0.AddDays(1), "Strong income");
        Assert.Equal("Strong income", loan.History.Last().Note);
    }

    [Fact]
    public void Reject_StoresReasonAsNote()
    {
        var loan = InState(LoanStatus.UnderReview);
        loan.Reject(OfficerId, T0.AddDays(1), "Insufficient income");
        Assert.Equal("Insufficient income", loan.History.Last().Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithBlankReason_ThrowsAndLeavesLoanUnchanged(string? reason)
    {
        var loan = InState(LoanStatus.UnderReview);
        var countBefore = loan.History.Count;

        Assert.Throws<ArgumentException>(() => loan.Reject(OfficerId, T0.AddDays(1), reason!));

        Assert.Equal(LoanStatus.UnderReview, loan.Status);
        Assert.Equal(countBefore, loan.History.Count);
    }

    // ---------- UTC guard ----------

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Submit_WithNonUtcTimestamp_Throws(DateTimeKind kind)
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0, kind);

        Assert.Throws<ArgumentException>(() =>
            Loan.Submit(ApplicantId, ApplicantUserId, 1000m, 12, "Car", at));
    }

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void ValidTransition_WithNonUtcTimestamp_ThrowsAndLeavesLoanUnchanged(LoanStatus from, Step step)
    {
        foreach (var kind in new[] { DateTimeKind.Unspecified, DateTimeKind.Local })
        {
            var loan = InState(from);
            var countBefore = loan.History.Count;
            var bad = new DateTime(T0.AddDays(1).Ticks, kind);

            Assert.Throws<ArgumentException>(() => Apply(loan, step, bad));

            Assert.Equal(from, loan.Status);
            Assert.Equal(countBefore, loan.History.Count);
        }
    }

    // ---------- Encapsulation ----------

    [Fact]
    public void History_CannotBeMutatedByCastingToList()
    {
        // Expected to FAIL until Loan.History returns _history.AsReadOnly().
        var loan = NewLoan();
        Assert.Null(loan.History as List<StatusHistory>);
    }
}