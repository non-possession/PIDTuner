using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class PreviewPlcTagSnapshotReader : IPlcTagSnapshotReader, IPlcTagSnapshotSessionReader
{
    public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return ReadSnapshots(configuration);
    }

    public Task<IPlcTagSnapshotReadSession> OpenSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IPlcTagSnapshotReadSession>(new PreviewPlcTagSnapshotReadSession(configuration));
    }

    private static Task<IReadOnlyList<PlcTagSnapshot>> ReadSnapshots(PlcProjectConfiguration configuration)
    {
        var now = DateTimeOffset.Now;
        var seconds = now.ToUnixTimeMilliseconds() / 1000.0;
        var snapshots = configuration.Tags
            .Where(tag => tag.IsEnabled)
            .Select(tag =>
            {
                var baseline = Math.Abs(HashCode.Combine(tag.Name, tag.Address)) % 100;
                var wave = Math.Sin(seconds + baseline / 10.0) * 3.0;
                var value = Math.Round((baseline + wave) * tag.Scale, 3);

                return new PlcTagSnapshot(
                    tag.Id,
                    tag.Name,
                    tag.Address,
                    value,
                    tag.Unit,
                    now,
                    "配置预览",
                    "Preview");
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<PlcTagSnapshot>>(snapshots);
    }

    private sealed class PreviewPlcTagSnapshotReadSession(PlcProjectConfiguration configuration) : IPlcTagSnapshotReadSession
    {
        public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            return ReadSnapshots(configuration);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
