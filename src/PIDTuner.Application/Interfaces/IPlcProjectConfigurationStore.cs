using PIDTuner.Domain.Configuration;

namespace PIDTuner.Application.Interfaces;

public interface IPlcProjectConfigurationStore
{
    Task<PlcProjectConfiguration> LoadAsync(Stream stream, CancellationToken cancellationToken);

    Task SaveAsync(PlcProjectConfiguration configuration, Stream stream, CancellationToken cancellationToken);
}
