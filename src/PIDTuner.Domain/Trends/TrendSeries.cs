namespace PIDTuner.Domain.Trends;

public sealed record TrendSeries(
    string Key,
    string DisplayName,
    IReadOnlyList<TrendPoint> Points);
