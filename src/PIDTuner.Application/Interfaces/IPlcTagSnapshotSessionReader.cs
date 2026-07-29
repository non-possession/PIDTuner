using PIDTuner.Domain.Configuration;

namespace PIDTuner.Application.Interfaces;

public interface IPlcTagSnapshotSessionReader
{
    Task<IPlcTagSnapshotReadSession> OpenSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken);
}
