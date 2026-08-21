using System.IO;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcRecordingWorkspaceViewModel
{
    private readonly PlcOneSecondRecorder _recorder;
    private readonly PlcDebugViewModel _debug;
    private readonly PlcLiveMonitorViewModel _liveMonitor;
    private readonly PlcLiveWorkspaceViewModel _liveWorkspace;
    private readonly HistoricalTrendViewModel _historicalTrend;
    private readonly PlcTrendWorkspaceViewModel _trendWorkspace;
    private readonly PlcReplayController _replayController;

    public PlcRecordingWorkspaceViewModel(
        PlcOneSecondRecorder recorder,
        PlcDebugViewModel debug,
        PlcLiveMonitorViewModel liveMonitor,
        PlcLiveWorkspaceViewModel liveWorkspace,
        HistoricalTrendViewModel historicalTrend,
        PlcTrendWorkspaceViewModel trendWorkspace)
    {
        _recorder = recorder;
        _debug = debug;
        _liveMonitor = liveMonitor;
        _liveWorkspace = liveWorkspace;
        _historicalTrend = historicalTrend;
        _trendWorkspace = trendWorkspace;
        _replayController = new PlcReplayController(debug, ApplyReplayOperation);
    }

    public event Action<RecordingWorkspaceNotification>? NotificationRequested;

    public async Task RecordOneSecondAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            StopReplay();
            _liveMonitor.MonitorStatus = "正在记录 1s 点位数据。";
            _liveMonitor.AcquisitionDiagnosticsStatus = "采集诊断：正在记录当前 1s 采集链路。";
            var result = await _recorder.RecordAsync(
                configuration,
                snapshots => _liveWorkspace.ApplySnapshots(snapshots, storeLiveHistory: false),
                cancellationToken);
            if (!result.IsSuccess)
            {
                Notify("无法记录 PLC 数据", "请至少启用一个可读取点位。", "Warning");
                return;
            }

            _historicalTrend.RememberFrames(result.Frames);
            _liveMonitor.MonitorStatus = result.MonitorStatus;
            _liveMonitor.AcquisitionDiagnosticsStatus = result.DiagnosticsStatus;
            Notify(
                "PLC 1s 记录完成",
                string.Join(
                    Environment.NewLine,
                    _liveMonitor.MonitorStatus,
                    result.DiagnosticsStatus,
                    $"保存位置：{result.RecordingPath}"),
                "Success");
        }
        catch (Exception exception)
        {
            _liveMonitor.MonitorStatus = $"1s 记录失败：{exception.Message}";
            Notify("PLC 1s 记录失败", exception.Message, "Error");
        }
    }

    public async Task LoadAsync(
        string fileName,
        bool showFullHistory,
        CancellationToken cancellationToken)
    {
        try
        {
            var loadResult = await _recorder.LoadAsync(fileName, cancellationToken);
            if (!loadResult.IsSuccess)
            {
                Notify("PLC 记录加载失败", "记录文件没有可回放的帧。", "Warning");
                return;
            }

            var recording = loadResult.Recording!;
            StopReplay();
            _historicalTrend.ClearLiveFrames();
            _debug.LoadReplay(recording.Frames, recording.IntervalMilliseconds);
            _historicalTrend.RememberFrames(recording.Frames);
            _historicalTrend.Workbench.SetRangeTextFromFrames(recording.Frames);

            _liveMonitor.ClearTags();
            _trendWorkspace.ResetTrend();
            if (showFullHistory)
            {
                _trendWorkspace.ShowLoadedReplayFrames();
            }
            else
            {
                _trendWorkspace.UseLiveMode();
                ApplyReplayOperation(_debug.ApplyReplayFrame(0, advance: true, "已定位"));
            }

            _liveMonitor.MonitorStatus =
                $"已加载 PLC 记录：{recording.FrameCount} 帧，{recording.SnapshotCount} 条快照，周期 {_debug.SourceReplayIntervalMilliseconds} ms。";
            _debug.UpdateReplayStatus("已加载");
            Notify(
                "PLC 记录已加载",
                string.Join(Environment.NewLine, _liveMonitor.MonitorStatus, $"文件位置：{Path.GetFullPath(fileName)}"),
                "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 记录加载失败", exception.Message, "Error");
        }
    }

    public void ToggleReplay() => _replayController.Toggle();

    public void StepBackward() => _replayController.StepBackward();

    public void StepForward() => _replayController.StepForward();

    public void SetSpeed(double speedMultiplier) => _replayController.SetSpeed(speedMultiplier);

    public void StopReplay() => _replayController.Stop();

    private void ApplyReplayOperation(PlcReplayOperationResult result)
    {
        if (result.ResetTrend)
        {
            _liveMonitor.ClearTags();
            _trendWorkspace.ResetTrend();
        }

        if (result.FramesToApply is not null)
        {
            foreach (var frame in result.FramesToApply)
            {
                _liveWorkspace.ApplySnapshots(frame, storeLiveHistory: false);
            }
        }

        if (result.FrameToApply is not null)
        {
            _liveWorkspace.ApplySnapshots(result.FrameToApply, storeLiveHistory: false);
        }

        if (!string.IsNullOrWhiteSpace(result.MonitorStatus))
        {
            _liveMonitor.MonitorStatus = result.MonitorStatus;
        }

        if (!string.IsNullOrWhiteSpace(result.NotificationTitle)
            && !string.IsNullOrWhiteSpace(result.NotificationMessage)
            && !string.IsNullOrWhiteSpace(result.NotificationKind))
        {
            Notify(result.NotificationTitle, result.NotificationMessage, result.NotificationKind);
        }
    }

    private void Notify(string title, string message, string kind) =>
        NotificationRequested?.Invoke(new RecordingWorkspaceNotification(title, message, kind));
}

public sealed record RecordingWorkspaceNotification(string Title, string Message, string Kind);
