using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class ConfiguredPlcTagSnapshotReader(
    IPlcTagSnapshotReader siemensS7Reader,
    IPlcTagSnapshotReader previewReader) : IPlcTagSnapshotReader
{
    public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.Protocol.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            return previewReader.ReadAsync(configuration, cancellationToken);
        }

        return siemensS7Reader.ReadAsync(configuration, cancellationToken);
    }
}
