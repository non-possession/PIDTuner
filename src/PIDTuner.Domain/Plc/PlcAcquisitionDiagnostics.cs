namespace PIDTuner.Domain.Plc;

public enum PlcAcquisitionFrameState
{
    Normal,
    Late,
    Timeout,
    Dropped,
    UiLagging
}

/// <summary>
/// Frame-level timing record for one PLC acquisition cycle. It keeps the planned time and
/// observed software-side timestamps together so acquisition jitter can be measured later.
/// </summary>
public sealed record PlcAcquisitionFrameDiagnostics(
    int FrameIndex,
    DateTimeOffset PlannedTimestampUtc,
    DateTimeOffset RequestStartedTimestampUtc,
    DateTimeOffset ResponseReceivedTimestampUtc,
    DateTimeOffset BufferedTimestampUtc,
    DateTimeOffset UiPresentedTimestampUtc,
    int SnapshotCount,
    PlcAcquisitionFrameState State,
    double? ActualIntervalMilliseconds = null,
    double? ResponseIntervalMilliseconds = null,
    double? PhaseErrorMilliseconds = null,
    bool CatchUpFrame = false)
{
    public double ScheduleDelayMilliseconds =>
        (RequestStartedTimestampUtc - PlannedTimestampUtc).TotalMilliseconds;

    public double ReadDurationMilliseconds =>
        (ResponseReceivedTimestampUtc - RequestStartedTimestampUtc).TotalMilliseconds;

    public double BufferDelayMilliseconds =>
        (BufferedTimestampUtc - ResponseReceivedTimestampUtc).TotalMilliseconds;

    public double UiDelayMilliseconds =>
        (UiPresentedTimestampUtc - BufferedTimestampUtc).TotalMilliseconds;
}

public sealed record PlcAcquisitionDiagnosticsSummary(
    int FrameCount,
    int SnapshotCount,
    double AverageScheduleDelayMilliseconds,
    double P95ScheduleDelayMilliseconds,
    double MaxScheduleDelayMilliseconds,
    double AverageReadDurationMilliseconds,
    double P95ReadDurationMilliseconds,
    double MaxReadDurationMilliseconds,
    int LateFrameCount,
    int TimeoutFrameCount,
    int DroppedFrameCount)
{
    public static PlcAcquisitionDiagnosticsSummary Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}

public static class PlcAcquisitionDiagnostics
{
    public static PlcAcquisitionDiagnosticsSummary Summarize(
        IReadOnlyList<PlcAcquisitionFrameDiagnostics> frames)
    {
        if (frames.Count == 0)
        {
            return PlcAcquisitionDiagnosticsSummary.Empty;
        }

        var scheduleDelays = frames
            .Select(frame => Math.Max(0, frame.ScheduleDelayMilliseconds))
            .Order()
            .ToArray();
        var readDurations = frames
            .Select(frame => Math.Max(0, frame.ReadDurationMilliseconds))
            .Order()
            .ToArray();

        return new PlcAcquisitionDiagnosticsSummary(
            frames.Count,
            frames.Sum(frame => frame.SnapshotCount),
            scheduleDelays.Average(),
            Percentile(scheduleDelays, 0.95d),
            scheduleDelays[^1],
            readDurations.Average(),
            Percentile(readDurations, 0.95d),
            readDurations[^1],
            frames.Count(frame => frame.State == PlcAcquisitionFrameState.Late),
            frames.Count(frame => frame.State == PlcAcquisitionFrameState.Timeout),
            frames.Count(frame => frame.State == PlcAcquisitionFrameState.Dropped));
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var rank = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Clamp(rank, 0, sortedValues.Count - 1)];
    }
}
