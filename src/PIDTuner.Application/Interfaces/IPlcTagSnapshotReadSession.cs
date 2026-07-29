using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcTagSnapshotReadSession : IAsyncDisposable
{
    Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken);
}
