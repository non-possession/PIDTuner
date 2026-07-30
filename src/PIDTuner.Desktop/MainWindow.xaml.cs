using System.Windows;
using System.Collections.Specialized;
using System.ComponentModel;
using PIDTuner.Desktop.Services;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly PlcTrendChartAdapter _plcTrendChartAdapter;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainWindowViewModel)DataContext;
        _plcTrendChartAdapter = new PlcTrendChartAdapter(PlcTrendPlot);
        _viewModel.PlcSnapshotsApplied += ApplyPlcTrendSnapshots;
        _viewModel.PlcTrendResetRequested += ResetPlcTrendChart;
        _viewModel.PlcMonitorTags.CollectionChanged += PlcMonitorTags_CollectionChanged;
        PlcTrendStatusTextBlock.Text = "趋势窗口：30s";
    }

    private void ResetPlcTrendChart()
    {
        _plcTrendChartAdapter.Clear();
        PlcTrendStatusTextBlock.Text = $"趋势窗口：{FormatTrendWindow(_plcTrendChartAdapter.VisibleWindow)}";
    }

    private void ApplyPlcTrendSnapshots(IReadOnlyList<PlcTagSnapshot> snapshots)
    {
        _plcTrendChartAdapter.AppendSnapshots(snapshots, _viewModel.PlcMonitorTags);
        PlcTrendStatusTextBlock.Text = $"趋势窗口：{FormatTrendWindow(_plcTrendChartAdapter.VisibleWindow)}";
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

        _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
    }

    private void PlcMonitorTag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcTagMonitorViewModel.IsTrendVisible))
        {
            _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
        }
    }

    private void PlcTrendWindow10Seconds_Click(object sender, RoutedEventArgs e) =>
        SetPlcTrendWindow(TimeSpan.FromSeconds(10));

    private void PlcTrendWindow30Seconds_Click(object sender, RoutedEventArgs e) =>
        SetPlcTrendWindow(TimeSpan.FromSeconds(30));

    private void PlcTrendWindow1Minute_Click(object sender, RoutedEventArgs e) =>
        SetPlcTrendWindow(TimeSpan.FromMinutes(1));

    private void PlcTrendWindow5Minutes_Click(object sender, RoutedEventArgs e) =>
        SetPlcTrendWindow(TimeSpan.FromMinutes(5));

    private void SetPlcTrendWindow(TimeSpan window)
    {
        _plcTrendChartAdapter.VisibleWindow = window;
        _plcTrendChartAdapter.Render(_viewModel.PlcMonitorTags);
        PlcTrendStatusTextBlock.Text = $"趋势窗口：{FormatTrendWindow(window)}";
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
        PlcTrendStatusTextBlock.Text = $"趋势窗口：{FormatTrendWindow(_plcTrendChartAdapter.VisibleWindow)}";
    }

    private static string FormatTrendWindow(TimeSpan window)
    {
        return window.TotalMinutes >= 1
            ? $"{window.TotalMinutes:0.#}min"
            : $"{window.TotalSeconds:0.#}s";
    }
}
