using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcHistoricalTrendStore
{
    string DatabasePath { get; }

    Task<IPlcHistoricalTrendWriteSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken);

    Task<(DateTimeOffset Start, DateTimeOffset End)?> GetAvailableRangeAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> QueryFramesAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumPointsPerTag,
        CancellationToken cancellationToken);
}

public interface IPlcHistoricalTrendWriteSession : IAsyncDisposable
{
    string DatabasePath { get; }

    void Enqueue(PlcAcquisitionFrame frame);

    Task<PlcHistoricalTrendWriteSummary> StopAsync(CancellationToken cancellationToken);
}

public sealed record PlcHistoricalTrendWriteSummary(
    string DatabasePath,
    int FrameCount,
    int SnapshotCount);
