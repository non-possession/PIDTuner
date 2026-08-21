using System.ComponentModel;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcTrendWorkspaceViewModel
{
    private readonly PlcTrendModeViewModel _mode;
    private readonly HistoricalTrendViewModel _historicalTrend;
    private readonly PlcLiveMonitorViewModel _liveMonitor;
    private readonly PlcLiveWorkspaceViewModel _liveWorkspace;
    private readonly PlcDebugViewModel _debug;
    private readonly Action _stopReplay;

    public PlcTrendWorkspaceViewModel(
        PlcTrendModeViewModel mode,
        HistoricalTrendViewModel historicalTrend,
        PlcLiveMonitorViewModel liveMonitor,
        PlcLiveWorkspaceViewModel liveWorkspace,
        PlcDebugViewModel debug,
        Action stopReplay)
    {
        _mode = mode;
        _historicalTrend = historicalTrend;
        _liveMonitor = liveMonitor;
        _liveWorkspace = liveWorkspace;
        _debug = debug;
        _stopReplay = stopReplay;

        _mode.PropertyChanged += Mode_PropertyChanged;
        Workbench.ViewportRequested += (start, end) => ViewportRequested?.Invoke(start, end);
        Workbench.YRangeRequested += (min, max) => LeftYRangeRequested?.Invoke(min, max);
        Workbench.RightYRangeRequested += (min, max) => RightYRangeRequested?.Invoke(min, max);
        Workbench.StatusRequested += ApplyWorkbenchStatus;
    }

    public event Action? TrendResetRequested;

    public event Action<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>>? FramesApplied;

    public event Action<DateTimeOffset?, DateTimeOffset?>? ViewportRequested;

    public event Action<double?, double?>? LeftYRangeRequested;

    public event Action<double?, double?>? RightYRangeRequested;

    public event Action<TrendWorkspaceNotification>? NotificationRequested;

    public HistoricalTrendWorkbenchViewModel Workbench => _historicalTrend.Workbench;

    public async Task ShowLiveAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!_liveMonitor.IsMonitoring)
        {
            await _liveWorkspace.StartAsync(configuration, resetHistory: false, cancellationToken);
        }

        _stopReplay();
        UseLiveMode();
        TrendResetRequested?.Invoke();
        _liveWorkspace.DrainNow();
    }

    public void UseLiveMode()
    {
        _mode.UseLiveMode();
        Workbench.Clear();
    }

    public void ToggleLivePause()
    {
        if (!_mode.IsHistoricalMode)
        {
            _mode.ToggleLiveScrollingPause();
        }
    }

    public void UseSingleAxisLayout() => Workbench.UseSingleAxisLayout();

    public void UseDualAxisLayout()
    {
        Workbench.UseDualAxisLayout();
        _liveMonitor.EnsureVisibleAxisGroups();
    }

    public async Task SetHistoricalWindowAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var end = Workbench.HasDataset ? Workbench.RangeEndValue : DateTimeOffset.Now;
        var frames = await _historicalTrend.LoadWindowAsync(
            end,
            window,
            _historicalTrend.CurrentFrames,
            cancellationToken);
        if (frames.Count > 0)
        {
            _historicalTrend.RememberFrames(frames);
            FramesApplied?.Invoke(frames);
        }

        ApplyHistoricalAction(_historicalTrend.SetVisibleWindow(window));
    }

    public async Task ShowHistoricalAsync(TimeSpan visibleWindow, CancellationToken cancellationToken)
    {
        _stopReplay();
        var end = DateTimeOffset.Now;
        var frames = await _historicalTrend.LoadWindowAsync(
            end,
            visibleWindow,
            _debug.LoadedReplayFrames,
            cancellationToken);
        if (frames.Count == 0)
        {
            _mode.UseHistoricalMode();
            return;
        }

        _historicalTrend.RememberFrames(frames);
        TrendResetRequested?.Invoke();
        ShowFrames(frames);
        _historicalTrend.SetWindowEndingAt(end, visibleWindow);
    }

    public async Task ApplyHistoricalRangeAsync(CancellationToken cancellationToken)
    {
        var result = await _historicalTrend.ApplySelectedRangeAsync(cancellationToken);
        if (result.Frames.Count > 0)
        {
            _historicalTrend.RememberFrames(result.Frames);
            FramesApplied?.Invoke(result.Frames);
        }

        ApplyHistoricalAction(result.Action);
    }

    public void ResetHistoricalRange() =>
        ApplyHistoricalAction(_historicalTrend.ResetTimeRange(_historicalTrend.CurrentFrames.Count));

    public void ApplyLeftYRange() => ApplyHistoricalAction(_historicalTrend.ApplyLeftYRange());

    public void ResetLeftYRange() => ApplyHistoricalAction(_historicalTrend.ResetLeftYRange());

    public void ResetRightYRange() => ApplyHistoricalAction(_historicalTrend.ResetRightYRange());

    public void ShowLoadedReplayFrames() => ShowFrames(_debug.LoadedReplayFrames);

    public void ShowFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        _mode.MarkHistoricalModeDisplayed();
        _historicalTrend.LoadFrames(frames);
        foreach (var frame in frames)
        {
            _liveWorkspace.ApplySnapshots(frame, applyTrend: false, storeLiveHistory: false);
        }

        FramesApplied?.Invoke(frames);
        _debug.MarkHistoricalReplayDisplayed();
        _liveMonitor.MonitorStatus = $"历史趋势已显示：{frames.Count} 帧。";
    }

    private void ApplyHistoricalAction(HistoricalTrendActionResult result)
    {
        if (!result.IsSuccess)
        {
            NotificationRequested?.Invoke(
                new TrendWorkspaceNotification(result.ErrorTitle!, result.ErrorMessage!, "Warning"));
            return;
        }

        _mode.UseHistoricalMode();
        _liveMonitor.MonitorStatus = result.Status!;
        if (!string.IsNullOrWhiteSpace(result.ReplayPhase))
        {
            _debug.UpdateReplayStatus(result.ReplayPhase);
        }
    }

    private void ApplyWorkbenchStatus(string message, string? replayPhase)
    {
        _liveMonitor.MonitorStatus = message;
        if (!string.IsNullOrWhiteSpace(replayPhase))
        {
            _debug.UpdateReplayStatus(replayPhase);
        }
    }

    private void Mode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _liveMonitor.IsLiveTrendPaused = _mode.IsLiveScrollingPaused;
    }
}

public sealed record TrendWorkspaceNotification(string Title, string Message, string Kind);
