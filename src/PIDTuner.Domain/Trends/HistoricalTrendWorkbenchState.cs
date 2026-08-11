namespace PIDTuner.Domain.Trends;

public sealed record HistoricalTrendWorkbenchState(
    HistoricalTrendDataset Dataset,
    TrendTimeRange? VisibleTimeRange,
    TrendNumericRange? VisibleYRange,
    IReadOnlySet<Guid> HiddenSeriesIds)
{
    public bool IsSeriesVisible(Guid seriesId) => !HiddenSeriesIds.Contains(seriesId);
}
