namespace PIDTuner.Domain.Trends;

public sealed record PidTrendSeries(
    TrendSeries SetPoint,
    TrendSeries ProcessValue,
    TrendSeries ManipulatedValue);
