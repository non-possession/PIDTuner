using System.Globalization;
using System.IO;
using System.Windows;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Plc;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using ScottPlotColor = ScottPlot.Color;
using ScottPlotColors = ScottPlot.Colors;

namespace PIDTuner.Desktop.Services;

public sealed class PlcTrendChartAdapter
{
    private const int MaxBufferedPointsPerTag = 6000;

    private readonly WpfPlot _plot;
    private readonly Dictionary<Guid, List<PlcTrendPoint>> _pointsByTag = [];
    private readonly ScottPlotColor[] _colors =
    [
        ScottPlotColors.CornflowerBlue,
        ScottPlotColors.MediumSeaGreen,
        ScottPlotColors.Orange,
        ScottPlotColors.MediumVioletRed,
        ScottPlotColors.Teal,
        ScottPlotColors.IndianRed,
        ScottPlotColors.Gold,
        ScottPlotColors.MediumPurple,
    ];

    private TimeSpan _visibleWindow = TimeSpan.FromSeconds(30);

    public PlcTrendChartAdapter(WpfPlot plot)
    {
        _plot = plot;
        ConfigurePlot();
    }

    public TimeSpan VisibleWindow
    {
        get => _visibleWindow;
        set => _visibleWindow = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
    }

    public void AppendSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
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

            points.Add(new PlcTrendPoint(snapshot.Timestamp, value));
            while (points.Count > MaxBufferedPointsPerTag)
            {
                points.RemoveAt(0);
            }
        }

        RemoveInactiveTags(monitorTags);
        Render(monitorTags);
    }

    public void Render(IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        _plot.Plot.Clear();
        ConfigurePlot();

        var latest = GetLatestTimestamp();
        if (latest is null)
        {
            _plot.Refresh();
            return;
        }

        var windowStart = latest.Value - VisibleWindow;
        var colorIndex = 0;
        foreach (var tag in monitorTags.Where(tag => tag.IsTrendVisible))
        {
            if (!_pointsByTag.TryGetValue(tag.TagId, out var points))
            {
                continue;
            }

            var visiblePoints = points
                .Where(point => point.Timestamp >= windowStart && point.Timestamp <= latest.Value)
                .ToArray();
            if (visiblePoints.Length == 0)
            {
                continue;
            }

            var xs = visiblePoints.Select(point => point.Timestamp.LocalDateTime.ToOADate()).ToArray();
            var ys = visiblePoints.Select(point => point.Value).ToArray();
            Scatter scatter = _plot.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = string.IsNullOrWhiteSpace(tag.Unit) ? tag.Name : $"{tag.Name} ({tag.Unit})";
            scatter.LineWidth = 2;
            scatter.MarkerSize = visiblePoints.Length > 200 ? 0 : 4;
            scatter.Color = _colors[colorIndex % _colors.Length];
            colorIndex++;
        }

        _plot.Plot.Axes.DateTimeTicksBottom();
        _plot.Plot.Axes.SetLimitsX(windowStart.LocalDateTime.ToOADate(), latest.Value.LocalDateTime.ToOADate());
        if (monitorTags.Any(tag => tag.IsTrendVisible && _pointsByTag.ContainsKey(tag.TagId)))
        {
            _plot.Plot.Axes.AutoScaleY();
        }

        _plot.Refresh();
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

    private void ConfigurePlot()
    {
        ConfigurePlotFonts();
        _plot.Plot.Title("PLC 实时趋势");
        _plot.Plot.XLabel("时间");
        _plot.Plot.YLabel("点位值");
        _plot.Plot.ShowLegend();
    }

    private void ConfigurePlotFonts()
    {
        RegisterFontFile("Microsoft YaHei", @"C:\Windows\Fonts\msyh.ttc");
        RegisterFontFile("SimHei", @"C:\Windows\Fonts\simhei.ttf");

        string[] fontCandidates =
        [
            "Microsoft YaHei",
            "SimHei",
            "Microsoft JhengHei",
            "Arial Unicode MS",
            "Noto Sans CJK SC",
        ];

        foreach (var fontName in fontCandidates)
        {
            if (ScottPlot.Fonts.GetTypeface(fontName, bold: false, italic: false) is not null)
            {
                _plot.Plot.Font.Set(fontName);
                return;
            }
        }
    }

    private static void RegisterFontFile(string fontName, string fontPath)
    {
        if (!File.Exists(fontPath))
        {
            return;
        }

        try
        {
            ScottPlot.Fonts.AddFontFile(fontName, fontPath, bold: false, italic: false);
        }
        catch
        {
            // Font registration is best effort; ScottPlot can still render with its default font.
        }
    }

    private void RemoveInactiveTags(IReadOnlyList<PlcTagMonitorViewModel> monitorTags)
    {
        var activeIds = monitorTags.Select(tag => tag.TagId).ToHashSet();
        foreach (var tagId in _pointsByTag.Keys.Where(tagId => !activeIds.Contains(tagId)).ToArray())
        {
            _pointsByTag.Remove(tagId);
        }
    }

    private DateTimeOffset? GetLatestTimestamp()
    {
        var points = _pointsByTag.Values
            .Where(points => points.Count > 0)
            .Select(points => points[^1].Timestamp)
            .ToArray();

        return points.Length == 0 ? null : points.Max();
    }

    private readonly record struct PlcTrendPoint(DateTimeOffset Timestamp, double Value);
}
