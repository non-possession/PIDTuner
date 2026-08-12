using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcLiveMonitorViewModel : INotifyPropertyChanged
{
    private const int UiRefreshMilliseconds = 250;

    private readonly PlcAcquisitionEngine _acquisitionEngine;
    private readonly PlcSampleBuffer _sampleBuffer = new();
    private bool _isMonitoring;
    private bool _isLiveTrendPaused;
    private int _currentAcquisitionIntervalMilliseconds;
    private PlcTagMonitorViewModel? _selectedTag;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlcLiveMonitorViewModel(PlcAcquisitionEngine acquisitionEngine)
    {
        _acquisitionEngine = acquisitionEngine;
        Tags.CollectionChanged += Tags_CollectionChanged;
    }

    public ObservableCollection<PlcTagMonitorViewModel> Tags { get; } = [];

    public IReadOnlyList<string> AxisGroups { get; } = ["Y1", "Y2"];

    public IReadOnlyList<PlcTagMonitorViewModel> LeftAxisTags =>
        Tags.Where(tag => tag.IsTrendVisible && tag.AxisGroup == "Y1").ToArray();

    public IReadOnlyList<PlcTagMonitorViewModel> RightAxisTags =>
        Tags.Where(tag => tag.IsTrendVisible && tag.AxisGroup == "Y2").ToArray();

    public PlcTagMonitorViewModel? SelectedTag
    {
        get => _selectedTag;
        set => SetProperty(ref _selectedTag, value);
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set => SetProperty(ref _isMonitoring, value);
    }

    public bool IsLiveTrendPaused
    {
        get => _isLiveTrendPaused;
        set => SetProperty(ref _isLiveTrendPaused, value);
    }

    public int CurrentAcquisitionIntervalMilliseconds
    {
        get => _currentAcquisitionIntervalMilliseconds;
        set => SetProperty(ref _currentAcquisitionIntervalMilliseconds, value);
    }

    public async Task<PlcLiveMonitorStartResult> StartAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var acquisitionIntervalMilliseconds = ResolveMonitoringIntervalMilliseconds(configuration);
        CurrentAcquisitionIntervalMilliseconds = acquisitionIntervalMilliseconds;
        _sampleBuffer.Clear();
        await _acquisitionEngine.StartAsync(
            configuration,
            TimeSpan.FromMilliseconds(acquisitionIntervalMilliseconds),
            _sampleBuffer,
            cancellationToken);
        IsMonitoring = true;

        return new PlcLiveMonitorStartResult(
            TimeSpan.FromMilliseconds(UiRefreshMilliseconds),
            $"点位监控运行中，采集周期 {acquisitionIntervalMilliseconds} ms，界面刷新 {UiRefreshMilliseconds} ms。");
    }

    public async Task StopAsync()
    {
        IsMonitoring = false;
        await _acquisitionEngine.StopAsync();
        _sampleBuffer.Clear();
    }

    public void ClearTags()
    {
        Tags.Clear();
        SelectedTag = null;
    }

    public void EnsureVisibleAxisGroups()
    {
        var visibleTags = Tags.Where(tag => tag.IsTrendVisible).ToArray();
        if (visibleTags.Length < 2)
        {
            return;
        }

        if (!visibleTags.Any(tag => tag.AxisGroup == "Y1"))
        {
            visibleTags[0].AxisGroup = "Y1";
        }

        if (!visibleTags.Any(tag => tag.AxisGroup == "Y2"))
        {
            visibleTags[^1].AxisGroup = "Y2";
        }

        RefreshAxisCandidateLists();
    }

    public PlcLiveMonitorDrainResult DrainPresentedFrames()
    {
        var frames = _sampleBuffer.Drain();
        if (frames.Count == 0)
        {
            return PlcLiveMonitorDrainResult.Empty;
        }

        var presentedFrames = new List<PlcAcquisitionFrame>(frames.Count);
        var diagnostics = new List<PlcAcquisitionFrameDiagnostics>(frames.Count);
        foreach (var frame in frames)
        {
            var presentedDiagnostics = frame.Diagnostics with { UiPresentedTimestampUtc = DateTimeOffset.UtcNow };
            presentedFrames.Add(frame with { Diagnostics = presentedDiagnostics });
            diagnostics.Add(presentedDiagnostics);
        }

        var lastFrame = frames[^1];
        var monitorStatus = lastFrame.Snapshots.Count == 0
            ? "实时采集中，尚未读取到启用点位。"
            : $"实时采集中，已应用 {frames.Count} 帧，最新 {lastFrame.Snapshots.Count} 个点位，数据源：{lastFrame.Snapshots[0].Source}。";

        return new PlcLiveMonitorDrainResult(
            presentedFrames,
            monitorStatus,
            FormatAcquisitionDiagnosticsSummary(PlcAcquisitionDiagnostics.Summarize(diagnostics)));
    }

    private static int ResolveMonitoringIntervalMilliseconds(PlcProjectConfiguration configuration)
    {
        return Math.Max(
            ResolveMinimumSamplingMilliseconds(configuration),
            configuration.DefaultSamplingMilliseconds);
    }

    private static int ResolveMinimumSamplingMilliseconds(PlcProjectConfiguration configuration)
    {
        return configuration.MinimumSamplingMilliseconds > 0
            ? configuration.MinimumSamplingMilliseconds
            : PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
    }

    private static string FormatAcquisitionDiagnosticsSummary(PlcAcquisitionDiagnosticsSummary summary)
    {
        if (summary.FrameCount == 0)
        {
            return "诊断：未记录采集帧。";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "诊断：调度延迟 avg {0:0.#} ms / P95 {1:0.#} ms / max {2:0.#} ms；读取耗时 avg {3:0.#} ms / P95 {4:0.#} ms；迟到 {5} 帧。",
            summary.AverageScheduleDelayMilliseconds,
            summary.P95ScheduleDelayMilliseconds,
            summary.MaxScheduleDelayMilliseconds,
            summary.AverageReadDurationMilliseconds,
            summary.P95ReadDurationMilliseconds,
            summary.LateFrameCount);
    }

    private void Tags_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (PlcTagMonitorViewModel tag in e.OldItems)
            {
                tag.PropertyChanged -= Tag_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (PlcTagMonitorViewModel tag in e.NewItems)
            {
                tag.PropertyChanged += Tag_PropertyChanged;
            }
        }

        RefreshAxisCandidateLists();
    }

    private void Tag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlcTagMonitorViewModel.AxisGroup) ||
            e.PropertyName == nameof(PlcTagMonitorViewModel.IsTrendVisible))
        {
            RefreshAxisCandidateLists();
        }
    }

    private void RefreshAxisCandidateLists()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LeftAxisTags)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RightAxisTags)));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record PlcLiveMonitorStartResult(
    TimeSpan UiRefreshInterval,
    string MonitorStatus);

public sealed record PlcLiveMonitorDrainResult(
    IReadOnlyList<PlcAcquisitionFrame> Frames,
    string MonitorStatus,
    string DiagnosticsStatus)
{
    public static PlcLiveMonitorDrainResult Empty { get; } = new(
        Array.Empty<PlcAcquisitionFrame>(),
        string.Empty,
        string.Empty);
}
