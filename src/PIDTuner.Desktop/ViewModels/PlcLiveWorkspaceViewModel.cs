using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcLiveWorkspaceViewModel
{
    private readonly PlcLiveMonitorViewModel _liveMonitor;
    private readonly HistoricalTrendViewModel _historicalTrend;
    private readonly PlcDebugViewModel _debug;
    private readonly PlcSnapshotSessionFactory _sessionFactory;
    private readonly PlcMonitorSnapshotPresenter _snapshotPresenter;
    private readonly PlcLiveMonitoringController _monitoringController;
    private readonly Func<bool> _shouldApplyLiveTrend;

    public PlcLiveWorkspaceViewModel(
        PlcLiveMonitorViewModel liveMonitor,
        HistoricalTrendViewModel historicalTrend,
        PlcDebugViewModel debug,
        PlcSnapshotSessionFactory sessionFactory,
        Func<bool> shouldApplyLiveTrend)
    {
        _liveMonitor = liveMonitor;
        _historicalTrend = historicalTrend;
        _debug = debug;
        _sessionFactory = sessionFactory;
        _shouldApplyLiveTrend = shouldApplyLiveTrend;
        _snapshotPresenter = new PlcMonitorSnapshotPresenter(liveMonitor.Tags);
        _snapshotPresenter.SnapshotsApplied += (snapshots, timestamp) =>
            SnapshotsApplied?.Invoke(snapshots, timestamp);
        _monitoringController = new PlcLiveMonitoringController(liveMonitor, ApplyBufferedFrames);
    }

    public event Action<IReadOnlyList<PlcTagSnapshot>, DateTimeOffset?>? SnapshotsApplied;

    public async Task<PlcLiveWorkspaceOperationResult> RefreshAsync(
        PlcProjectConfiguration configuration,
        bool isHistoricalMode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isHistoricalMode)
            {
                _liveMonitor.ClearTags();
            }

            if (_liveMonitor.IsMonitoring)
            {
                _monitoringController.DrainNow();
                return new PlcLiveWorkspaceOperationResult(isHistoricalMode, isHistoricalMode, null, null, null);
            }

            var snapshots = await _sessionFactory.ReadOnceAsync(configuration, cancellationToken);
            ApplySnapshots(snapshots);
            _liveMonitor.MonitorStatus = snapshots.Count == 0
                ? "没有启用的监控点位。"
                : $"已刷新 {snapshots.Count} 个点位，数据源：{snapshots[0].Source}。";
            return new PlcLiveWorkspaceOperationResult(isHistoricalMode, isHistoricalMode, null, null, null);
        }
        catch (Exception exception)
        {
            _liveMonitor.MonitorStatus = $"刷新失败：{exception.Message}";
            return PlcLiveWorkspaceOperationResult.Error("PLC 点位刷新失败", exception.Message);
        }
    }

    public async Task StartAsync(
        PlcProjectConfiguration configuration,
        bool resetHistory,
        CancellationToken cancellationToken)
    {
        if (_liveMonitor.IsMonitoring)
        {
            return;
        }

        if (resetHistory)
        {
            _historicalTrend.ClearLiveFrames();
        }

        var result = await _monitoringController.StartAsync(configuration, cancellationToken);
        _liveMonitor.MonitorStatus = result.MonitorStatus;
        _liveMonitor.AcquisitionDiagnosticsStatus =
            $"历史数据：SQLite 写入中，{result.HistoricalDatabasePath}";
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var summary = await _monitoringController.StopAsync();
        if (summary is not null)
        {
            _liveMonitor.AcquisitionDiagnosticsStatus =
                $"实时采集已停止，SQLite 写入已关闭，已写入 {summary.FrameCount} 帧 / " +
                $"{summary.SnapshotCount} 条点位值，{summary.DatabasePath}";
        }

        _liveMonitor.MonitorStatus = "点位监控已停止。";
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void DrainNow() => _monitoringController.DrainNow();

    public void ApplySnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? trendTimestamp = null,
        bool applyTrend = true,
        bool storeLiveHistory = true)
    {
        _snapshotPresenter.SelectedTag = _liveMonitor.SelectedTag;
        _snapshotPresenter.Apply(snapshots, trendTimestamp, applyTrend);
        _liveMonitor.SelectedTag = _snapshotPresenter.SelectedTag;
        if (storeLiveHistory)
        {
            _historicalTrend.ObserveSnapshots(
                snapshots,
                trendTimestamp,
                _liveMonitor.CurrentAcquisitionIntervalMilliseconds);
        }
    }

    private void ApplyBufferedFrames(PlcLiveMonitorDrainResult result)
    {
        foreach (var frame in result.Frames)
        {
            ApplySnapshots(
                frame.Snapshots,
                frame.Diagnostics.PlannedTimestampUtc,
                applyTrend: _shouldApplyLiveTrend());
            _debug.EnqueueDiagnosticsFrame(frame);
        }

        _liveMonitor.MonitorStatus = result.MonitorStatus;
        _liveMonitor.AcquisitionDiagnosticsStatus = result.DiagnosticsStatus;
    }
}

public sealed record PlcLiveWorkspaceOperationResult(
    bool ShouldUseLiveMode,
    bool ShouldResetTrend,
    string? NotificationTitle,
    string? NotificationMessage,
    string? NotificationKind)
{
    public static PlcLiveWorkspaceOperationResult Error(string title, string message) =>
        new(false, false, title, message, "Error");
}
