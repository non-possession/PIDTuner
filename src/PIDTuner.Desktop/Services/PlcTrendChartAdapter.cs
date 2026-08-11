using System.Globalization;
using System.Windows;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Plc;
using ScottPlot;
using ScottPlot.WPF;

namespace PIDTuner.Desktop.Services;

public sealed class PlcTrendChartAdapter
{
    private static readonly TimeSpan MinimumLiveRetentionPadding = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultUiRefreshInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultLiveSamplingInterval = TimeSpan.FromMilliseconds(250);

    private readonly WpfPlot _plot;
    private readonly SharedScottPlotTrendRenderer _renderer = new();
    private readonly Dictionary<Guid, List<PlcTrendPoint>> _pointsByTag = [];

    private TimeSpan _visibleWindow = TimeSpan.FromSeconds(30);
    private TimeSpan _maxLiveTrendWindow = TimeSpan.FromMinutes(5);
    private TimeSpan _uiRefreshInterval = DefaultUiRefreshInterval;
    private TimeSpan _liveSamplingInterval = DefaultLiveSamplingInterval;
    private bool _showFullHistory;
    private bool _isLiveScrollingPaused;
    private DateTimeOffset? _historicalVisibleStart;
    private DateTimeOffset? _historicalVisibleEnd;
    private (double Min, double Max)? _manualYRange;

    public PlcTrendChartAdapter(WpfPlot plot)
    {
        _plot = plot;
        _renderer.ConfigurePlot(_plot, ShowFullHistory);
    }

    public TimeSpan VisibleWindow
    {
        get => _visibleWindow;
        set => _visibleWindow = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
    }

    public TimeSpan MaxLiveTrendWindow
    {
        get => _maxLiveTrendWindow;
        set => _maxLiveTrendWindow = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
    }

    public TimeSpan UiRefreshInterval
    {
        get => _uiRefreshInterval;
        set => _uiRefreshInterval = value <= TimeSpan.Zero ? DefaultUiRefreshInterval : value;
    }

    public TimeSpan LiveSamplingInterval
    {
        get => _liveSamplingInterval;
        set => _liveSamplingInterval = value <= TimeSpan.Zero ? DefaultLiveSamplingInterval : value;
    }

    public bool ShowFullHistory
    {
        get => _showFullHistory;
        set => _showFullHistory = value;
    }

    public bool IsLiveScrollingPaused
    {
        get => _isLiveScrollingPaused;
        set => _isLiveScrollingPaused = value;
    }

    public void SetHistoricalVisibleRange(DateTimeOffset? start, DateTimeOffset? end)
    {
        _historicalVisibleStart = start;
        _historicalVisibleEnd = end;
    }

    public void SetManualYRange(double min, double max)
    {
        _manualYRange = (min, max);
    }

    public void ClearManualYRange()
    {
        _manualYRange = null;
    }

    public void Clear()
    {
        _pointsByTag.Clear();
        _plot.Plot.Clear();
        _renderer.ConfigurePlot(_plot, ShowFullHistory);
        _plot.Refresh();
    }

    public void AppendSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        IReadOnlyList<PlcTagMonitorViewModel> monitorTags,
        DateTimeOffset? timestampOverride = null)
    {
        AppendPoints(snapshots, timestampOverride);
        RemoveInactiveTags(monitorTags);
        TrimLivePoints();
        if (IsLiveScrollingPaused && !ShowFullHistory)
        {
            return;
        }

        Render(monitorTags);
    }

    public void AppendSnapshotFrames(
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames,
        IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        foreach (var frame in frames)
        {
            AppendPoints(frame, timestampOverride: null);
        }

        RemoveInactiveTags(monitorTags);
        TrimLivePoints();
        Render(monitorTags);
    }

    public void Render(IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        var range = GetTimestampRange();
        if (range is null)
        {
            _plot.Plot.Clear();
            _renderer.ConfigurePlot(_plot, ShowFullHistory);
            _plot.Refresh();
            return;
        }

        var latest = range.Value.Latest;
        var (windowStart, windowEnd) = GetVisibleTimeRange(range.Value.Earliest, latest);
        _renderer.Render(
            _plot,
            _pointsByTag,
            monitorTags,
            windowStart,
            windowEnd,
            _manualYRange,
            ShowFullHistory);
    }

    public void AutoFitY()
    {
        ClearManualYRange();
        _plot.Plot.Axes.AutoScaleY();
        _plot.Refresh();
    }

    public PlcTrendVisibleExport CreateVisibleExport(IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        var xLimits = _plot.Plot.Axes.GetLimits().XRange;
        var visibleStart = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.FromOADate(xLimits.Min), DateTimeKind.Local));
        var visibleEnd = new DateTimeOffset(
            DateTime.SpecifyKind(DateTime.FromOADate(xLimits.Max), DateTimeKind.Local));
        var points = new List<PlcTrendVisibleExportPoint>();

        foreach (var tag in monitorTags.Where(tag => tag.IsTrendVisible))
        {
            if (!_pointsByTag.TryGetValue(tag.TagId, out var tagPoints))
            {
                continue;
            }

            points.AddRange(tagPoints
                .Where(point => point.Timestamp >= visibleStart && point.Timestamp <= visibleEnd)
                .Select(point => new PlcTrendVisibleExportPoint(
                    point.Timestamp,
                    tag.TagId,
                    tag.Name,
                    tag.Address,
                    point.Value,
                    tag.Unit,
                    tag.Quality,
                    tag.Source)));
        }

        return new PlcTrendVisibleExport(
            visibleStart,
            visibleEnd,
            ShowFullHistory,
            points
                .OrderBy(point => point.Timestamp)
                .ThenBy(point => point.TagName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public static TimeSpan CalculateLiveRetentionWindow(
        TimeSpan maxLiveTrendWindow,
        TimeSpan uiRefreshInterval,
        TimeSpan liveSamplingInterval)
    {
        var padding = Max(
            MinimumLiveRetentionPadding,
            Max(uiRefreshInterval * 2, liveSamplingInterval * 5));
        return EnsurePositive(maxLiveTrendWindow, TimeSpan.FromSeconds(1)) + padding;
    }

    public string BuildNearestPointSummary(
        Point position,
        Size controlSize,
        IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        if (controlSize.Width <= 0 || controlSize.Height <= 0 || _pointsByTag.Count == 0)
        {
            return string.Empty;
        }

        Coordinates coordinates = _plot.Plot.GetCoordinates(
            (float)position.X,
            (float)position.Y,
            _plot.Plot.Axes.Bottom,
            _plot.Plot.Axes.Left);
        var target = DateTime.SpecifyKind(DateTime.FromOADate(coordinates.X), DateTimeKind.Local);
        var summaries = new List<string>();

        foreach (var tag in monitorTags.Where(tag => tag.IsTrendVisible))
        {
            if (!_pointsByTag.TryGetValue(tag.TagId, out var points))
            {
                continue;
            }

            var nearest = points
                .OrderBy(point => Math.Abs((point.Timestamp.LocalDateTime - target).Ticks))
                .FirstOrDefault();
            if (nearest.Timestamp == default)
            {
                continue;
            }

            var unit = string.IsNullOrWhiteSpace(tag.Unit) ? string.Empty : $" {tag.Unit}";
            summaries.Add($"{tag.Name}={nearest.Value.ToString("0.###", CultureInfo.InvariantCulture)}{unit}");
        }

        return summaries.Count == 0
            ? string.Empty
            : $"光标 {target:HH:mm:ss.fff} | 最近值：{string.Join(" | ", summaries.Take(4))}";
    }

    private void RemoveInactiveTags(IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        var activeIds = monitorTags.Select(tag => tag.TagId).ToHashSet();
        foreach (var tagId in _pointsByTag.Keys.Where(tagId => !activeIds.Contains(tagId)).ToArray())
        {
            _pointsByTag.Remove(tagId);
        }
    }

    private void AppendPoints(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? timestampOverride)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Value is not double value || double.IsNaN(value) || double.IsInfinity(value))
            {
                continue;
            }

            if (!_pointsByTag.TryGetValue(snapshot.TagId, out var points))
            {
                points = [];
                _pointsByTag.Add(snapshot.TagId, points);
            }

            points.Add(new PlcTrendPoint(timestampOverride ?? snapshot.Timestamp, value));
        }
    }

    private void TrimLivePoints()
    {
        if (ShowFullHistory)
        {
            return;
        }

        var range = GetTimestampRange();
        if (range is null)
        {
            return;
        }

        var retentionWindow = CalculateLiveRetentionWindow(
            MaxLiveTrendWindow,
            UiRefreshInterval,
            LiveSamplingInterval);
        var cutoff = range.Value.Latest - retentionWindow;
        foreach (var points in _pointsByTag.Values)
        {
            var removeCount = points.FindIndex(point => point.Timestamp >= cutoff);
            if (removeCount < 0)
            {
                points.Clear();
                continue;
            }

            if (removeCount > 0)
            {
                points.RemoveRange(0, removeCount);
            }
        }
    }

    private (DateTimeOffset Earliest, DateTimeOffset Latest)? GetTimestampRange()
    {
        var points = _pointsByTag.Values
            .Where(points => points.Count > 0)
            .SelectMany(points => points.Select(point => point.Timestamp))
            .ToArray();

        return points.Length == 0 ? null : (points.Min(), points.Max());
    }

    private (DateTimeOffset Start, DateTimeOffset End) GetVisibleTimeRange(
        DateTimeOffset earliest,
        DateTimeOffset latest)
    {
        if (!ShowFullHistory)
        {
            return (latest - VisibleWindow, latest);
        }

        var start = _historicalVisibleStart ?? earliest;
        var end = _historicalVisibleEnd ?? latest;
        if (start < earliest)
        {
            start = earliest;
        }

        if (end > latest)
        {
            end = latest;
        }

        return start <= end ? (start, end) : (earliest, latest);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private static TimeSpan EnsurePositive(TimeSpan value, TimeSpan fallback) => value > TimeSpan.Zero ? value : fallback;
}
