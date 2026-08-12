using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using PIDTuner.Desktop.Services;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MaxLiveTrendWindow = TimeSpan.FromMinutes(5);

    private readonly MainWindowViewModel _viewModel;
    private readonly LivePlcTrendAdapter _plcTrendChartAdapter;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainWindowViewModel)DataContext;
        _plcTrendChartAdapter = new LivePlcTrendAdapter(PlcTrendPlot);
        ConfigurePlcTrendRetention();
        _viewModel.PlcSnapshotsApplied += ApplyPlcTrendSnapshots;
        _viewModel.PlcSnapshotFramesApplied += ApplyPlcTrendSnapshotFrames;
        _viewModel.PlcTrendResetRequested += ResetPlcTrendChart;
        _viewModel.PlcHistoricalViewportRequested += ApplyPlcHistoricalViewport;
        _viewModel.PlcTrendYRangeRequested += ApplyPlcTrendYRange;
        _viewModel.PlcTrendRightYRangeRequested += ApplyPlcTrendRightYRange;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.LiveMonitor.Tags.CollectionChanged += PlcMonitorTags_CollectionChanged;
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ResetPlcTrendChart()
    {
        ApplyPlcTrendChartState();
        _plcTrendChartAdapter.Clear();
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ApplyPlcTrendSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? trendTimestamp)
    {
        ConfigurePlcTrendRetention();
        ApplyPlcTrendChartState();
        _plcTrendChartAdapter.AppendSnapshots(snapshots, _viewModel.LiveMonitor.Tags, trendTimestamp);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ApplyPlcTrendSnapshotFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        ConfigurePlcTrendRetention();
        ApplyPlcTrendChartState();
        _plcTrendChartAdapter.AppendSnapshotFrames(frames, _viewModel.LiveMonitor.Tags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.PlcTrendMode) &&
            e.PropertyName != nameof(MainWindowViewModel.HistoricalTrendWorkbench))
        {
            return;
        }

        ApplyPlcTrendChartState();
        if (!_viewModel.PlcTrendMode.IsLiveScrollingPaused)
        {
            _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        }

        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void PlcMonitorTags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (PlcTagMonitorViewModel item in e.OldItems)
            {
                item.PropertyChanged -= PlcMonitorTag_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (PlcTagMonitorViewModel item in e.NewItems)
            {
                item.PropertyChanged += PlcMonitorTag_PropertyChanged;
            }
        }

        ApplyPlcTrendChartState();
        EnsureSelectedHistoricalAxisSeries();
        _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
    }

    private void PlcMonitorTag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcTagMonitorViewModel.IsTrendVisible) ||
            e.PropertyName == nameof(PlcTagMonitorViewModel.AxisGroup))
        {
            if (_viewModel.HistoricalTrendWorkbench.IsDualAxisLayout)
            {
                _viewModel.LiveMonitor.EnsureVisibleAxisGroups();
            }

            EnsureSelectedHistoricalAxisSeries();
            ApplyPlcTrendChartState();
            _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        }
    }

    private async void PlcTrendWindow10Seconds_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromSeconds(10));

    private async void PlcTrendWindow30Seconds_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromSeconds(30));

    private async void PlcTrendWindow1Minute_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromMinutes(1));

    private async void PlcTrendWindow5Minutes_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromMinutes(5));

    private async void PlcTrendWindow10Minutes_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromMinutes(10));

    private async void PlcTrendWindow30Minutes_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromMinutes(30));

    private async void PlcTrendWindow1Hour_Click(object sender, RoutedEventArgs e) =>
        await SetPlcTrendWindowAsync(TimeSpan.FromHours(1));

    private void ApplyPlcHistoricalViewport(DateTimeOffset? start, DateTimeOffset? end)
    {
        ApplyPlcTrendChartState();
        _plcTrendChartAdapter.SetHistoricalVisibleRange(start, end);
        _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ApplyPlcTrendYRange(double? min, double? max)
    {
        if (min.HasValue && max.HasValue)
        {
            _plcTrendChartAdapter.SetManualYRange(min.Value, max.Value);
        }
        else
        {
            _plcTrendChartAdapter.ClearManualYRange();
        }

        _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ApplyPlcTrendRightYRange(double? min, double? max)
    {
        if (min.HasValue && max.HasValue)
        {
            _plcTrendChartAdapter.SetManualRightYRange(min.Value, max.Value);
        }
        else
        {
            _plcTrendChartAdapter.ClearManualRightYRange();
        }

        _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private async void PlcTrendExportVisible_Click(object sender, RoutedEventArgs e)
    {
        var export = _plcTrendChartAdapter.CreateVisibleExport(_viewModel.LiveMonitor.Tags);
        await _viewModel.ExportVisiblePlcTrendAsync(export);
    }

    private async Task SetPlcTrendWindowAsync(TimeSpan window)
    {
        if (_viewModel.PlcTrendMode.IsHistoricalMode)
        {
            await _viewModel.SetPlcHistoricalTrendWindowAsync(window);
            return;
        }

        var wasHistoricalTrendMode = _viewModel.PlcTrendMode.IsHistoricalMode;
        _viewModel.UsePlcLiveTrendMode();
        ConfigurePlcTrendRetention();
        _plcTrendChartAdapter.VisibleWindow = window;
        ApplyPlcTrendChartState();
        if (wasHistoricalTrendMode)
        {
            await _viewModel.ShowPlcLiveTrendAsync();
            return;
        }

        _plcTrendChartAdapter.Render(_viewModel.LiveMonitor.Tags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void PlcTrendPlot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var summary = _plcTrendChartAdapter.BuildNearestPointSummary(
            e.GetPosition(PlcTrendPlot),
            new Size(PlcTrendPlot.ActualWidth, PlcTrendPlot.ActualHeight),
            _viewModel.LiveMonitor.Tags);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            PlcTrendStatusTextBlock.Text = summary;
        }
    }

    private void PlcTrendPlot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private string BuildTrendStatusText()
    {
        return _viewModel.PlcTrendMode.IsHistoricalMode
            ? "历史趋势：静态查看"
            : _viewModel.PlcTrendMode.IsLiveScrollingPaused
                ? $"趋势窗口：{FormatTrendWindow(_plcTrendChartAdapter.VisibleWindow)}（滚动已暂停，采集继续）"
            : $"趋势窗口：{FormatTrendWindow(_plcTrendChartAdapter.VisibleWindow)}";
    }

    private static string FormatTrendWindow(TimeSpan window)
    {
        return window.TotalMinutes >= 1
            ? $"{window.TotalMinutes:0.#}min"
            : $"{window.TotalSeconds:0.#}s";
    }

    private void ConfigurePlcTrendRetention()
    {
        _plcTrendChartAdapter.MaxLiveTrendWindow = MaxLiveTrendWindow;
        _plcTrendChartAdapter.UiRefreshInterval =
            TimeSpan.FromMilliseconds(MainWindowViewModel.LiveMonitorUiRefreshMilliseconds);
        _plcTrendChartAdapter.LiveSamplingInterval =
            TimeSpan.FromMilliseconds(_viewModel.CurrentPlcAcquisitionIntervalMilliseconds);
    }

    private void ApplyPlcTrendChartState()
    {
        _plcTrendChartAdapter.ShowFullHistory = _viewModel.PlcTrendMode.IsHistoricalMode;
        _plcTrendChartAdapter.IsLiveScrollingPaused = _viewModel.PlcTrendMode.IsLiveScrollingPaused;
        _plcTrendChartAdapter.IsDualAxisLayout = _viewModel.HistoricalTrendWorkbench.IsDualAxisLayout;
    }

    private void EnsureSelectedHistoricalAxisSeries()
    {
        _viewModel.HistoricalTrendWorkbench.EnsureSelectedAxisSeries(
            _viewModel.LiveMonitor.LeftAxisTags.Select(tag => tag.TagId).ToArray(),
            _viewModel.LiveMonitor.RightAxisTags.Select(tag => tag.TagId).ToArray());
    }
}
