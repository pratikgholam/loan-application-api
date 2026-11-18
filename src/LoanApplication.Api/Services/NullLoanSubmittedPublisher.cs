using LoanApplication.Api.Messaging;

namespace LoanApplication.Api.Services;

/// <summary>
/// Used when RabbitMQ is not configured (local unit tests, or an API started
/// without a broker). Submissions stay saved but unassigned.
/// </summary>
public sealed class NullLoanSubmittedPublisher : ILoanSubmittedPublisher
{
    public Task PublishAsync(LoanSubmitted message, CancellationToken ct) => Task.CompletedTask;
}
