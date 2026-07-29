using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcTagSnapshotReader
{
    Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken);
}
