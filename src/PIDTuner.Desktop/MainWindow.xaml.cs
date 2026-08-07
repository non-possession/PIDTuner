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
    private readonly PlcTrendChartAdapter _plcTrendChartAdapter;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainWindowViewModel)DataContext;
        _plcTrendChartAdapter = new PlcTrendChartAdapter(PlcTrendPlot);
        ConfigurePlcTrendRetention();
        _viewModel.PlcSnapshotsApplied += ApplyPlcTrendSnapshots;
        _viewModel.PlcTrendResetRequested += ResetPlcTrendChart;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.PlcMonitorTags.CollectionChanged += PlcMonitorTags_CollectionChanged;
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ResetPlcTrendChart()
    {
        _plcTrendChartAdapter.ShowFullHistory = _viewModel.IsPlcHistoricalTrendMode;
        _plcTrendChartAdapter.Clear();
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ApplyPlcTrendSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? trendTimestamp)
    {
        ConfigurePlcTrendRetention();
        _plcTrendChartAdapter.ShowFullHistory = _viewModel.IsPlcHistoricalTrendMode;
        _plcTrendChartAdapter.IsLiveScrollingPaused = _viewModel.IsPlcLiveTrendPaused;
        _plcTrendChartAdapter.AppendSnapshots(snapshots, _viewModel.PlcMonitorTags, trendTimestamp);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsPlcLiveTrendPaused) &&
            e.PropertyName != nameof(MainWindowViewModel.IsPlcHistoricalTrendMode))
        {
            return;
        }

        _plcTrendChartAdapter.ShowFullHistory = _viewModel.IsPlcHistoricalTrendMode;
        _plcTrendChartAdapter.IsLiveScrollingPaused = _viewModel.IsPlcLiveTrendPaused;
        if (!_viewModel.IsPlcLiveTrendPaused)
        {
            _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
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

        _plcTrendChartAdapter.ShowFullHistory = _viewModel.IsPlcHistoricalTrendMode;
        _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
    }

    private void PlcMonitorTag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcTagMonitorViewModel.IsTrendVisible))
        {
            _plcTrendChartAdapter.ShowFullHistory = _viewModel.IsPlcHistoricalTrendMode;
            _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
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

    private void PlcTrendFitY_Click(object sender, RoutedEventArgs e)
    {
        _plcTrendChartAdapter.AutoFitY();
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private async Task SetPlcTrendWindowAsync(TimeSpan window)
    {
        var wasHistoricalTrendMode = _viewModel.IsPlcHistoricalTrendMode;
        _viewModel.UsePlcLiveTrendMode();
        ConfigurePlcTrendRetention();
        _plcTrendChartAdapter.VisibleWindow = window;
        _plcTrendChartAdapter.ShowFullHistory = false;
        _plcTrendChartAdapter.IsLiveScrollingPaused = false;
        if (wasHistoricalTrendMode)
        {
            await _viewModel.ShowPlcLiveTrendAsync();
            return;
        }

        _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
        PlcTrendStatusTextBlock.Text = BuildTrendStatusText();
    }

    private void PlcTrendPlot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var summary = _plcTrendChartAdapter.BuildNearestPointSummary(
            e.GetPosition(PlcTrendPlot),
            new Size(PlcTrendPlot.ActualWidth, PlcTrendPlot.ActualHeight),
            _viewModel.PlcMonitorTags);
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
        return _viewModel.IsPlcHistoricalTrendMode
            ? "历史趋势：静态查看"
            : _viewModel.IsPlcLiveTrendPaused
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
}
