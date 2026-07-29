using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface IPidParameterSetRepository
{
    Task SaveAsync(PidParameterSet parameterSet, CancellationToken cancellationToken);

    Task<IReadOnlyList<PidParameterSet>> ListAsync(CancellationToken cancellationToken);
}
