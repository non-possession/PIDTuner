using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface ITestSessionRepository
{
    Task SaveAsync(TestSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<TestSession>> ListAsync(CancellationToken cancellationToken);
}
