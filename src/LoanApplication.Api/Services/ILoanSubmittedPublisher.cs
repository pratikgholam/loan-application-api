using LoanApplication.Api.Messaging;

namespace LoanApplication.Api.Services;

/// <summary>Publishes a loan submission so a worker can assign an officer.</summary>
public interface ILoanSubmittedPublisher
{
    Task PublishAsync(LoanSubmitted message, CancellationToken ct);
}
