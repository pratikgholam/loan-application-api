namespace LoanApplication.Api.Messaging;

/// <summary>
/// Event published after a loan application is saved. The worker uses the loan type
/// to pick the officer whose expertise matches.
/// </summary>
public sealed record LoanSubmitted(Guid LoanId, Entities.LoanType LoanType)
{
    public const string QueueName = "loan-submitted";

    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);
}
