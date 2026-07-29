namespace PIDTuner.Domain.Analysis;

public sealed record PidResponseMetrics(
    double? OvershootPercent,
    TimeSpan? RiseTime,
    TimeSpan? SettlingTime,
    double? SteadyStateError,
    double? PeakProcessValue = null,
    TimeSpan? PeakTime = null,
    double? MinimumProcessValue = null,
    double? MeanAbsoluteError = null,
    double? MeanSquaredError = null,
    double? IntegralAbsoluteError = null,
    double? OutputStandardDeviation = null,
    bool? HasSustainedOscillation = null,
    bool? HasOutputSaturation = null);
