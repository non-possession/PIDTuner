using System.ComponentModel;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class HistoricalTrendViewModel
{
    private readonly PlcHistoricalTrendCoordinator _coordinator;

    public HistoricalTrendViewModel(PlcHistoricalTrendCoordinator coordinator)
    {
        _coordinator = coordinator;
        Workbench = new HistoricalTrendWorkbenchViewModel();
    }

    public HistoricalTrendWorkbenchViewModel Workbench { get; }

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Workbench.PropertyChanged += value;
        remove => Workbench.PropertyChanged -= value;
    }

    public void ObserveLiveFrame(PlcAcquisitionFrame frame, int samplingIntervalMilliseconds) =>
        _coordinator.ObserveLiveFrame(frame, samplingIntervalMilliseconds);

    public void ObserveSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? timestamp,
        int samplingIntervalMilliseconds) =>
        _coordinator.ObserveSnapshots(snapshots, timestamp, samplingIntervalMilliseconds);

    public void ClearLiveFrames() => _coordinator.ClearLiveFrames();

    public async Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> LoadWindowAsync(
        DateTimeOffset end,
        TimeSpan window,
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> fallbackFrames,
        CancellationToken cancellationToken)
    {
        var start = end - window;
        var frames = await _coordinator.LoadRangeAsync(start, end, cancellationToken);
        if (frames.Count == 0)
        {
            frames = fallbackFrames;
        }

        LoadFrames(frames);
        return frames;
    }

    public async Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> LoadSelectedRangeAsync(
        CancellationToken cancellationToken)
    {
        var start = Workbench.RangeStartValue;
        var end = Workbench.RangeEndValue;
        var frames = await _coordinator.LoadRangeAsync(start, end, cancellationToken);
        LoadFrames(frames);
        if (frames.Count > 0)
        {
            Workbench.RangeStartValue = start;
            Workbench.RangeEndValue = end;
        }

        return frames;
    }

    public void LoadFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        if (frames.Count > 0)
        {
            Workbench.LoadFrames(frames);
        }
    }

    public HistoricalTrendActionResult SetVisibleWindow(TimeSpan window)
    {
        if (!Workbench.HasDataset)
        {
            return HistoricalTrendActionResult.Failure(
                "无法调整历史趋势窗口",
                "请先切换到历史趋势，并确保已有采集数据或历史记录。");
        }

        if (!Workbench.TrySetVisibleDuration(window, out var error))
        {
            return HistoricalTrendActionResult.Failure(
                "历史趋势窗口无效",
                error ?? "当前没有可用的历史趋势数据。");
        }

        return HistoricalTrendActionResult.Success(
            $"历史趋势窗口已调整为 {FormatWindow(window)}。",
            "历史窗口视图");
    }

    public async Task<HistoricalTrendLoadActionResult> ApplySelectedRangeAsync(
        CancellationToken cancellationToken)
    {
        var frames = await LoadSelectedRangeAsync(cancellationToken);
        if (!Workbench.HasDataset)
        {
            return new HistoricalTrendLoadActionResult(
                frames,
                HistoricalTrendActionResult.Failure(
                    "无法调整历史趋势区间",
                    "请先切换到历史趋势，并确保已有采集数据。"));
        }

        if (!Workbench.TryApplyRangeText(out var error))
        {
            return new HistoricalTrendLoadActionResult(
                frames,
                HistoricalTrendActionResult.Failure(
                    "历史趋势区间无效",
                    error ?? "请输入可识别的时间。"));
        }

        if (Workbench.VisibleSeries.Count == 0)
        {
            return new HistoricalTrendLoadActionResult(
                frames,
                HistoricalTrendActionResult.Failure(
                    "历史趋势区间无数据",
                    "当前时间范围不在已采集的 PLC 记录内。"));
        }

        return new HistoricalTrendLoadActionResult(
            frames,
            HistoricalTrendActionResult.Success(
                $"历史趋势视图已调整：{Workbench.RangeStartText} - {Workbench.RangeEndText}。",
                "历史窗口视图"));
    }

    public HistoricalTrendActionResult ResetTimeRange(int frameCount)
    {
        if (!Workbench.HasDataset)
        {
            return HistoricalTrendActionResult.Failure(
                "无法恢复历史趋势范围",
                "请先切换到历史趋势，并确保已有采集数据。");
        }

        Workbench.ResetTimeRangeToFull();
        return HistoricalTrendActionResult.Success(
            $"历史趋势已恢复全量视图，{frameCount} 帧。",
            "全量历史");
    }

    public HistoricalTrendActionResult ApplyLeftYRange()
    {
        if (!Workbench.TryApplyYText(out var error))
        {
            return HistoricalTrendActionResult.Failure(
                "Y 轴范围无效",
                error ?? "请同时输入可识别的 Y 最小值和最大值。");
        }

        return HistoricalTrendActionResult.Success(
            $"左侧 Y 轴范围已调整：{Workbench.YMinimumText} - {Workbench.YMaximumText}。");
    }

    public HistoricalTrendActionResult ResetLeftYRange()
    {
        Workbench.ResetYRangeToFull();
        return HistoricalTrendActionResult.Success("左侧 Y 轴已恢复自动量程。");
    }

    public HistoricalTrendActionResult ResetRightYRange()
    {
        Workbench.ResetRightYRangeToFull();
        return HistoricalTrendActionResult.Success("右侧 Y2 轴已恢复当前参考变量量程。");
    }

    public void SetWindowEndingAt(DateTimeOffset end, TimeSpan window)
    {
        if (!Workbench.HasDataset)
        {
            return;
        }

        var start = end - window;
        Workbench.SetVisibleTimeRange(start, end);
        Workbench.ApplyVisibleTimeRangeToViewport(start, end);
    }

    private static string FormatWindow(TimeSpan window) =>
        window.TotalHours >= 1
            ? $"{window.TotalHours:0.#} h"
            : window.TotalMinutes >= 1
                ? $"{window.TotalMinutes:0.#} min"
                : $"{window.TotalSeconds:0.#} s";
}

public sealed record HistoricalTrendLoadActionResult(
    IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> Frames,
    HistoricalTrendActionResult Action);

public sealed record HistoricalTrendActionResult(
    bool IsSuccess,
    string? Status,
    string? ReplayPhase,
    string? ErrorTitle,
    string? ErrorMessage)
{
    public static HistoricalTrendActionResult Success(string status, string? replayPhase = null) =>
        new(true, status, replayPhase, null, null);

    public static HistoricalTrendActionResult Failure(string title, string message) =>
        new(false, null, null, title, message);
}
