using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface IPidSampleRepository
{
    Task SaveBatchAsync(IReadOnlyCollection<PidSample> samples, CancellationToken cancellationToken);

    Task<IReadOnlyList<PidSample>> GetBySessionAsync(Guid testSessionId, CancellationToken cancellationToken);
}
