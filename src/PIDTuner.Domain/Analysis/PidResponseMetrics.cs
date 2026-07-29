namespace PIDTuner.Domain.Analysis;

public sealed record PidResponseMetrics(
    double? OvershootPercent,
    TimeSpan? RiseTime,
    TimeSpan? SettlingTime,
    double? SteadyStateError);
