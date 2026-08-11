namespace PIDTuner.Domain.Trends;

public sealed class HistoricalTrendWorkbenchCoordinator
{
    public HistoricalTrendWorkbenchState LoadDataset(HistoricalTrendDataset dataset)
    {
        return new HistoricalTrendWorkbenchState(
            dataset,
            CreateFullTimeRange(dataset),
            CreateFullYRange(dataset),
            new HashSet<Guid>());
    }

    public HistoricalTrendWorkbenchState SetVisibleTimeRange(
        HistoricalTrendWorkbenchState state,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var fullRange = CreateFullTimeRange(state.Dataset);
        if (fullRange is null)
        {
            return state with { VisibleTimeRange = null };
        }

        var clampedStart = start < fullRange.Start ? fullRange.Start : start;
        var clampedEnd = end > fullRange.End ? fullRange.End : end;
        if (clampedStart > clampedEnd)
        {
            return state with { VisibleTimeRange = fullRange };
        }

        return state with { VisibleTimeRange = new TrendTimeRange(clampedStart, clampedEnd) };
    }

    public HistoricalTrendWorkbenchState ResetVisibleTimeRange(HistoricalTrendWorkbenchState state)
    {
        return state with { VisibleTimeRange = CreateFullTimeRange(state.Dataset) };
    }

    public HistoricalTrendWorkbenchState SetVisibleYRange(
        HistoricalTrendWorkbenchState state,
        double minimum,
        double maximum)
    {
        if (double.IsNaN(minimum) || double.IsInfinity(minimum) ||
            double.IsNaN(maximum) || double.IsInfinity(maximum))
        {
            return state;
        }

        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        if (Math.Abs(maximum - minimum) < double.Epsilon)
        {
            maximum = minimum + 1d;
        }

        return state with { VisibleYRange = new TrendNumericRange(minimum, maximum) };
    }

    public HistoricalTrendWorkbenchState ResetVisibleYRange(HistoricalTrendWorkbenchState state)
    {
        return state with { VisibleYRange = CreateFullYRange(state.Dataset) };
    }

    public HistoricalTrendWorkbenchState SetSeriesVisibility(
        HistoricalTrendWorkbenchState state,
        Guid seriesId,
        bool isVisible)
    {
        var hidden = state.HiddenSeriesIds.ToHashSet();
        if (isVisible)
        {
            hidden.Remove(seriesId);
        }
        else
        {
            hidden.Add(seriesId);
        }

        return state with { HiddenSeriesIds = hidden };
    }

    public IReadOnlyList<HistoricalTrendSeries> GetVisibleSeries(HistoricalTrendWorkbenchState state)
    {
        var timeRange = state.VisibleTimeRange;
        return state.Dataset.Series
            .Where(series => state.IsSeriesVisible(series.SeriesId))
            .Select(series => series with
            {
                Points = timeRange is null
                    ? series.Points
                    : series.Points
                        .Where(point => point.Timestamp >= timeRange.Start && point.Timestamp <= timeRange.End)
                        .ToArray()
            })
            .Where(series => series.Points.Count > 0)
            .ToArray();
    }

    private static TrendTimeRange? CreateFullTimeRange(HistoricalTrendDataset dataset)
    {
        return dataset.Start is { } start && dataset.End is { } end
            ? new TrendTimeRange(start, end)
            : null;
    }

    private static TrendNumericRange? CreateFullYRange(HistoricalTrendDataset dataset)
    {
        var values = dataset.Series
            .SelectMany(series => series.Points)
            .Select(point => point.Value)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }

        var min = values.Min();
        var max = values.Max();
        if (Math.Abs(max - min) < double.Epsilon)
        {
            max = min + 1d;
        }

        return new TrendNumericRange(min, max);
    }
}
