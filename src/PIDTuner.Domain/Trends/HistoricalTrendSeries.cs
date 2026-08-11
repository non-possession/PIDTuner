namespace PIDTuner.Domain.Trends;

public sealed record HistoricalTrendSeries(
    Guid SeriesId,
    string Name,
    string Address,
    string? Unit,
    IReadOnlyList<HistoricalTrendPoint> Points);
