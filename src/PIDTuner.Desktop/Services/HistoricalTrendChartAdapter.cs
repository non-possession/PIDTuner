using PIDTuner.Domain.Trends;
using ScottPlot.WPF;

namespace PIDTuner.Desktop.Services;

public sealed class HistoricalTrendChartAdapter
{
    private readonly WpfPlot _plot;
    private readonly SharedScottPlotTrendRenderer _renderer = new();

    public HistoricalTrendChartAdapter(WpfPlot plot)
    {
        _plot = plot;
        _renderer.ConfigurePlot(_plot, isHistoricalMode: true);
    }

    public void Clear()
    {
        _plot.Plot.Clear();
        _renderer.ConfigurePlot(_plot, isHistoricalMode: true);
        _plot.Refresh();
    }

    public void Render(HistoricalTrendWorkbenchState state)
    {
        if (state.VisibleTimeRange is null)
        {
            Clear();
            return;
        }

        var series = state.Dataset.Series
            .Where(item => state.IsSeriesVisible(item.SeriesId))
            .Select(item => new PlcTrendRenderSeries(
                item.SeriesId,
                item.Name,
                item.Unit,
                "Y1",
                item.Points
                    .Select(point => new PlcTrendPoint(point.Timestamp, point.Value))
                    .ToArray()))
            .ToArray();

        _renderer.Render(
            _plot,
            series,
            state.VisibleTimeRange.Start,
            state.VisibleTimeRange.End,
            state.VisibleYRange is null ? null : (state.VisibleYRange.Minimum, state.VisibleYRange.Maximum),
            null,
            isHistoricalMode: true);
    }
}
