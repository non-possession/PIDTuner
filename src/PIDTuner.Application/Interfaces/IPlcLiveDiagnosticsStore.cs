using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcLiveDiagnosticsStore
{
    Task<IPlcLiveDiagnosticsSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        TimeSpan duration,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlcLiveDiagnosticsSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> LoadSessionFramesAsync(
        Guid sessionId,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken cancellationToken);
}

public sealed record PlcLiveDiagnosticsSessionInfo(
    Guid SessionId,
    string ConfigurationName,
    string Protocol,
    string IpAddress,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset? StoppedAtUtc,
    int DefaultSamplingMilliseconds,
    int MinimumSamplingMilliseconds,
    int FrameCount,
    int SnapshotCount);

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
    int LateFrameCount,
    int ReadOperationCount,
    double AverageReadOperationDurationMilliseconds,
    double MaxReadOperationDurationMilliseconds,
    int SlowReadOperationCount,
    int DiagnosticsQueueDroppedFrameCount = 0);
