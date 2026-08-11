namespace PIDTuner.Domain.Trends;

public sealed record HistoricalTrendDataset(IReadOnlyList<HistoricalTrendSeries> Series)
{
    public bool IsEmpty => Series.Count == 0 || Series.All(series => series.Points.Count == 0);

    public DateTimeOffset? Start => IsEmpty
        ? null
        : Series.SelectMany(series => series.Points).Min(point => point.Timestamp);

    public DateTimeOffset? End => IsEmpty
        ? null
        : Series.SelectMany(series => series.Points).Max(point => point.Timestamp);
}
