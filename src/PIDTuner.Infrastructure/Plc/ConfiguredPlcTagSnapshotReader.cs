using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class ConfiguredPlcTagSnapshotReader(
    IPlcTagSnapshotReader siemensS7Reader,
    IPlcTagSnapshotReader previewReader) : IPlcTagSnapshotReader, IPlcTagSnapshotSessionReader
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

    public Task<IPlcTagSnapshotReadSession> OpenSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var reader = configuration.Protocol.Contains("preview", StringComparison.OrdinalIgnoreCase)
            ? previewReader
            : siemensS7Reader;

        if (reader is IPlcTagSnapshotSessionReader sessionReader)
        {
            return sessionReader.OpenSessionAsync(configuration, cancellationToken);
        }

        return Task.FromResult<IPlcTagSnapshotReadSession>(
            new SingleReadSnapshotSession(reader, configuration));
    }

    private sealed class SingleReadSnapshotSession(
        IPlcTagSnapshotReader reader,
        PlcProjectConfiguration configuration) : IPlcTagSnapshotReadSession
    {
        public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            return reader.ReadAsync(configuration, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
