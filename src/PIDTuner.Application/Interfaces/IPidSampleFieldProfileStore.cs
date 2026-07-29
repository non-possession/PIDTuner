using PIDTuner.Domain.Configuration;

namespace PIDTuner.Application.Interfaces;

public interface IPidSampleFieldProfileStore
{
    Task<PidSampleFieldProfile> LoadAsync(Stream stream, CancellationToken cancellationToken);

    Task SaveAsync(PidSampleFieldProfile profile, Stream stream, CancellationToken cancellationToken);
}
