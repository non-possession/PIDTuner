using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcDebugViewModel : INotifyPropertyChanged
{
    private string _diagnosticsStatus = "实时诊断：尚未启动。";
    private string _replayStatus = "尚未加载 PLC 记录。";
    private bool _isDiagnosticsRunning;
    private bool _isReplayRunning;
    private double _replaySpeedMultiplier = 1d;
    private IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> _loadedReplayFrames = Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
    private int _replayNextFrameIndex;
    private int _replayDisplayedFrameIndex = -1;
    private int _sourceReplayIntervalMilliseconds = 100;

    public PlcDebugViewModel(ObservableCollection<PlcTagMonitorViewModel> detailedTags)
    {
        DetailedTags = detailedTags;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlcTagMonitorViewModel> DetailedTags { get; }

    public IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> LoadedReplayFrames
    {
        get => _loadedReplayFrames;
        private set
        {
            if (SetProperty(ref _loadedReplayFrames, value))
            {
                OnPropertyChanged(nameof(HasReplayFrames));
            }
        }
    }

    public int SourceReplayIntervalMilliseconds
    {
        get => _sourceReplayIntervalMilliseconds;
        private set
        {
            if (SetProperty(ref _sourceReplayIntervalMilliseconds, value))
            {
                OnPropertyChanged(nameof(EffectiveReplayIntervalMilliseconds));
            }
        }
    }

    public int ReplayNextFrameIndex
    {
        get => _replayNextFrameIndex;
        private set => SetProperty(ref _replayNextFrameIndex, value);
    }

    public int ReplayDisplayedFrameIndex
    {
        get => _replayDisplayedFrameIndex;
        private set
        {
            if (SetProperty(ref _replayDisplayedFrameIndex, value))
            {
                OnPropertyChanged(nameof(DisplayedReplayFrameNumber));
            }
        }
    }

    public bool HasReplayFrames => LoadedReplayFrames.Count > 0;

    public int DisplayedReplayFrameNumber => ReplayDisplayedFrameIndex >= 0 ? ReplayDisplayedFrameIndex + 1 : 0;

    public int EffectiveReplayIntervalMilliseconds =>
        Math.Max(10, (int)Math.Round(SourceReplayIntervalMilliseconds / ReplaySpeedMultiplier));

    public string ReplaySpeedText => $"{ReplaySpeedMultiplier:0.##}x";

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        set => SetProperty(ref _diagnosticsStatus, value);
    }

    public string ReplayStatus
    {
        get => _replayStatus;
        set => SetProperty(ref _replayStatus, value);
    }

    public bool IsDiagnosticsRunning
    {
        get => _isDiagnosticsRunning;
        set => SetProperty(ref _isDiagnosticsRunning, value);
    }

    public bool IsReplayRunning
    {
        get => _isReplayRunning;
        private set => SetProperty(ref _isReplayRunning, value);
    }

    public double ReplaySpeedMultiplier
    {
        get => _replaySpeedMultiplier;
        private set
        {
            if (SetProperty(ref _replaySpeedMultiplier, value))
            {
                OnPropertyChanged(nameof(ReplaySpeedText));
                OnPropertyChanged(nameof(EffectiveReplayIntervalMilliseconds));
            }
        }
    }

    public void LoadReplay(
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames,
        int sourceIntervalMilliseconds)
    {
        LoadedReplayFrames = frames;
        SourceReplayIntervalMilliseconds = Math.Max(10, sourceIntervalMilliseconds);
        ReplayNextFrameIndex = 0;
        ReplayDisplayedFrameIndex = -1;
        IsReplayRunning = false;
        UpdateReplayStatus("已加载");
    }

    public PlcReplayOperationResult StartReplay()
    {
        if (!HasReplayFrames)
        {
            return PlcReplayOperationResult.Warning("无法回放 PLC 记录", "请先打开一个 PLC 记录 JSON 文件。");
        }

        var resetTrend = false;
        if (ReplayNextFrameIndex >= LoadedReplayFrames.Count)
        {
            ReplayNextFrameIndex = 0;
            ReplayDisplayedFrameIndex = -1;
            resetTrend = true;
        }

        IsReplayRunning = true;
        UpdateReplayStatus("播放中");
        return PlcReplayOperationResult.Status(
            $"PLC 记录回放中：源周期 {SourceReplayIntervalMilliseconds} ms，速度 {ReplaySpeedText}，下一帧 {ReplayNextFrameIndex + 1}/{LoadedReplayFrames.Count}。",
            resetTrend);
    }

    public PlcReplayOperationResult PauseReplay()
    {
        IsReplayRunning = false;
        UpdateReplayStatus("已暂停");
        return PlcReplayOperationResult.Status(
            $"PLC 记录回放已暂停：第 {DisplayedReplayFrameNumber}/{LoadedReplayFrames.Count} 帧。");
    }

    public PlcReplayOperationResult StepBackward()
    {
        if (!HasReplayFrames)
        {
            return PlcReplayOperationResult.Warning("无法控制 PLC 回放", "请先打开一个 PLC 记录 JSON 文件。");
        }

        IsReplayRunning = false;
        var targetFrameIndex = Math.Max(0, ReplayDisplayedFrameIndex - 1);
        ReplayDisplayedFrameIndex = targetFrameIndex;
        ReplayNextFrameIndex = Math.Min(targetFrameIndex + 1, LoadedReplayFrames.Count);
        UpdateReplayStatus("单帧后退");
        return new PlcReplayOperationResult(
            $"PLC 记录回放：已回到第 {targetFrameIndex + 1}/{LoadedReplayFrames.Count} 帧。",
            ResetTrend: true,
            FrameToApply: null,
            FramesToApply: LoadedReplayFrames.Take(targetFrameIndex + 1).ToArray(),
            NotificationTitle: null,
            NotificationMessage: null,
            NotificationKind: null);
    }

    public PlcReplayOperationResult StepForward()
    {
        if (!HasReplayFrames)
        {
            return PlcReplayOperationResult.Warning("无法控制 PLC 回放", "请先打开一个 PLC 记录 JSON 文件。");
        }

        IsReplayRunning = false;
        if (ReplayNextFrameIndex >= LoadedReplayFrames.Count)
        {
            UpdateReplayStatus("已到末尾");
            return PlcReplayOperationResult.Status(
                $"PLC 记录回放已在最后一帧：{LoadedReplayFrames.Count}/{LoadedReplayFrames.Count}。");
        }

        return ApplyReplayFrame(ReplayNextFrameIndex, advance: true, "单帧前进");
    }

    public PlcReplayOperationResult ApplyNextReplayFrame()
    {
        if (ReplayNextFrameIndex >= LoadedReplayFrames.Count)
        {
            IsReplayRunning = false;
            UpdateReplayStatus("回放完成");
            var status = $"PLC 记录回放完成：{LoadedReplayFrames.Count} 帧。";
            return new PlcReplayOperationResult(
                status,
                ResetTrend: false,
                FrameToApply: null,
                FramesToApply: null,
                NotificationTitle: "PLC 记录回放完成",
                NotificationMessage: status,
                NotificationKind: "Success");
        }

        return ApplyReplayFrame(ReplayNextFrameIndex, advance: true, IsReplayRunning ? "播放中" : "已定位");
    }

    public PlcReplayOperationResult ApplyReplayFrame(int frameIndex, bool advance, string phase)
    {
        if (!HasReplayFrames)
        {
            return PlcReplayOperationResult.Status(string.Empty);
        }

        var index = Math.Clamp(frameIndex, 0, LoadedReplayFrames.Count - 1);
        var frame = LoadedReplayFrames[index];
        ReplayDisplayedFrameIndex = index;
        if (advance)
        {
            ReplayNextFrameIndex = Math.Min(index + 1, LoadedReplayFrames.Count);
        }

        UpdateReplayStatus(phase);
        return new PlcReplayOperationResult(
            $"PLC 记录回放：第 {index + 1}/{LoadedReplayFrames.Count} 帧，{frame.Count} 个点位。",
            ResetTrend: false,
            FrameToApply: frame,
            FramesToApply: null,
            NotificationTitle: null,
            NotificationMessage: null,
            NotificationKind: null);
    }

    public void MarkHistoricalReplayDisplayed()
    {
        ReplayDisplayedFrameIndex = Math.Max(0, LoadedReplayFrames.Count - 1);
        ReplayNextFrameIndex = LoadedReplayFrames.Count;
        UpdateReplayStatus("历史趋势");
    }

    public void StopReplay()
    {
        IsReplayRunning = false;
    }

    public void SetReplaySpeed(double speedMultiplier)
    {
        ReplaySpeedMultiplier = Math.Clamp(speedMultiplier, 0.5d, 5d);
        UpdateReplayStatus("速度已调整");
    }

    public void UpdateReplayStatus(string phase)
    {
        if (!HasReplayFrames)
        {
            ReplayStatus = "尚未加载 PLC 记录。";
            return;
        }

        ReplayStatus =
            $"{phase}：第 {DisplayedReplayFrameNumber}/{LoadedReplayFrames.Count} 帧，源周期 {SourceReplayIntervalMilliseconds} ms，播放间隔 {EffectiveReplayIntervalMilliseconds} ms，速度 {ReplaySpeedText}";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record PlcReplayOperationResult(
    string MonitorStatus,
    bool ResetTrend,
    IReadOnlyList<PlcTagSnapshot>? FrameToApply,
    IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>? FramesToApply,
    string? NotificationTitle,
    string? NotificationMessage,
    string? NotificationKind)
{
    public static PlcReplayOperationResult Status(string monitorStatus, bool resetTrend = false) =>
        new(monitorStatus, resetTrend, null, null, null, null, null);

    public static PlcReplayOperationResult Warning(string title, string message) =>
        new(string.Empty, false, null, null, title, message, "Warning");
}
