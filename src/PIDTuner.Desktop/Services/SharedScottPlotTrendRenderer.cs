using System.IO;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using ScottPlotColor = ScottPlot.Color;
using ScottPlotColors = ScottPlot.Colors;

namespace PIDTuner.Desktop.Services;

/// <summary>
/// Shared ScottPlot rendering implementation used by live and historical trend adapters.
/// The caller owns data state; this module owns plot styling, series drawing, and axis application.
/// </summary>
internal sealed class SharedScottPlotTrendRenderer
{
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

    public void ConfigurePlot(WpfPlot plot, bool isHistoricalMode)
    {
        ConfigurePlotFonts(plot);
        plot.Plot.Title(isHistoricalMode ? "PLC 历史趋势" : "PLC 实时趋势");
        plot.Plot.XLabel("时间");
        plot.Plot.YLabel("点位值");
        plot.Plot.ShowLegend();
    }

    public void Render(
        WpfPlot plot,
        IReadOnlyList<PlcTrendRenderSeries> series,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        (double Min, double Max)? manualYRange,
        bool isHistoricalMode)
    {
        plot.Plot.Clear();
        ConfigurePlot(plot, isHistoricalMode);

        var colorIndex = 0;
        foreach (var item in series)
        {
            var visiblePoints = item.Points
                .Where(point => point.Timestamp >= windowStart && point.Timestamp <= windowEnd)
                .ToArray();
            if (visiblePoints.Length == 0)
            {
                continue;
            }

            var xs = visiblePoints.Select(point => point.Timestamp.LocalDateTime.ToOADate()).ToArray();
            var ys = visiblePoints.Select(point => point.Value).ToArray();
            Scatter scatter = plot.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = string.IsNullOrWhiteSpace(item.Unit) ? item.Name : $"{item.Name} ({item.Unit})";
            scatter.LineWidth = 2;
            scatter.MarkerSize = visiblePoints.Length > 200 ? 0 : 4;
            scatter.Color = _colors[colorIndex % _colors.Length];
            colorIndex++;
        }

        plot.Plot.Axes.DateTimeTicksBottom();
        var xStart = windowStart.LocalDateTime.ToOADate();
        var xEnd = windowEnd.LocalDateTime.ToOADate();
        if (xStart >= xEnd)
        {
            xStart = windowStart.AddMilliseconds(-500).LocalDateTime.ToOADate();
            xEnd = windowEnd.AddMilliseconds(500).LocalDateTime.ToOADate();
        }

        plot.Plot.Axes.SetLimitsX(xStart, xEnd);
        if (series.Any(item => item.Points.Count > 0))
        {
            if (manualYRange is { } yRange)
            {
                plot.Plot.Axes.SetLimitsY(yRange.Min, yRange.Max);
            }
            else
            {
                plot.Plot.Axes.AutoScaleY();
            }
        }

        plot.Refresh();
    }

    private static void ConfigurePlotFonts(WpfPlot plot)
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
                plot.Plot.Font.Set(fontName);
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
}
