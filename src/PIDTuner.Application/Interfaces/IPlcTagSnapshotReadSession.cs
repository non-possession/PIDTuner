using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcTagSnapshotReadSession : IAsyncDisposable
{
    IReadOnlyList<PlcReadOperationDiagnostics> LastReadDiagnostics => Array.Empty<PlcReadOperationDiagnostics>();

    Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken);
}
