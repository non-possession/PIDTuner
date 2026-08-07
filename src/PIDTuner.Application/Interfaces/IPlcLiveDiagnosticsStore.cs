using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcLiveDiagnosticsStore
{
    Task<IPlcLiveDiagnosticsSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        TimeSpan duration,
        CancellationToken cancellationToken);
}

public interface IPlcLiveDiagnosticsSession : IAsyncDisposable
{
    Guid SessionId { get; }

    string DatabasePath { get; }

    DateTimeOffset StartedAtUtc { get; }

    DateTimeOffset EndsAtUtc { get; }

    void Enqueue(PlcAcquisitionFrame frame);

    Task<PlcLiveDiagnosticsSummary> StopAsync(CancellationToken cancellationToken);
}

public sealed record PlcLiveDiagnosticsSummary(
    Guid SessionId,
    string DatabasePath,
    int FrameCount,
    int SnapshotCount,
    double AverageScheduleDelayMilliseconds,
    double MaxScheduleDelayMilliseconds,
    double AverageReadDurationMilliseconds,
    double MaxReadDurationMilliseconds,
    int LateFrameCount);
