namespace LoanApplication.Api.Entities;

public class Applicant
{
    private readonly List<Loan> _loans = new();
 
    private Applicant() { } // required by EF Core
 
    public Applicant(Guid userId, string fullName, DateOnly dateOfBirth, decimal monthlyIncome)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));
        if (monthlyIncome < 0)
            throw new ArgumentOutOfRangeException(nameof(monthlyIncome), "Income cannot be negative.");
 
        Id = Guid.NewGuid();
        UserId = userId;
        FullName = fullName.Trim();
        DateOfBirth = dateOfBirth;
        MonthlyIncome = monthlyIncome;
    }
 
    public Guid Id { get; private set; }
 
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
 
    public string FullName { get; private set; } = null!;
    public DateOnly DateOfBirth { get; private set; }
    public decimal MonthlyIncome { get; private set; }
 
    public IReadOnlyCollection<Loan> Loans => _loans;
}
