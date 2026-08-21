using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcSnapshotSessionFactory(IPlcTagSnapshotReader reader)
{
    public Task<IPlcTagSnapshotReadSession> OpenAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return reader is IPlcTagSnapshotSessionReader sessionReader
            ? sessionReader.OpenSessionAsync(configuration, cancellationToken)
            : Task.FromResult<IPlcTagSnapshotReadSession>(
                new SingleReadSnapshotSession(reader, configuration));
    }

    public Task<IReadOnlyList<PlcTagSnapshot>> ReadOnceAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken) =>
        reader.ReadAsync(configuration, cancellationToken);

    private sealed class SingleReadSnapshotSession(
        IPlcTagSnapshotReader reader,
        PlcProjectConfiguration configuration) : IPlcTagSnapshotReadSession
    {
        public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken) =>
            reader.ReadAsync(configuration, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
