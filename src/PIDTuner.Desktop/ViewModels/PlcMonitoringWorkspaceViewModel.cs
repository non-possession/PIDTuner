namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcMonitoringWorkspaceViewModel(
    PlcConfigurationEditorViewModel configurationEditor,
    PlcLiveMonitorViewModel liveMonitor,
    PlcLiveWorkspaceViewModel liveWorkspace,
    PlcTrendModeViewModel trendMode,
    PlcTrendWorkspaceViewModel trendWorkspace,
    PlcRecordingWorkspaceViewModel recordingWorkspace,
    PlcDiagnosticsWorkspaceViewModel diagnosticsWorkspace)
{
    public event Action? TrendResetRequested;

    public event Action<WorkspaceOperationResult>? NotificationRequested;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await liveWorkspace.RefreshAsync(
            configurationEditor.BuildConfiguration(),
            trendMode.IsHistoricalMode,
            cancellationToken);
        if (result.ShouldUseLiveMode)
        {
            trendMode.UseLiveMode();
        }

        if (result.ShouldResetTrend)
        {
            TrendResetRequested?.Invoke();
        }

        if (result.Notification is not null)
        {
            NotificationRequested?.Invoke(result.Notification);
        }
    }

    public async Task ToggleMonitoringAsync(CancellationToken cancellationToken)
    {
        if (liveMonitor.IsMonitoring)
        {
            await liveWorkspace.StopAsync(cancellationToken);
            await diagnosticsWorkspace.StopAsync(
                "实时监控已停止，诊断写入已关闭。",
                cancellationToken);
            return;
        }

        recordingWorkspace.StopReplay();
        await liveWorkspace.StartAsync(
            configurationEditor.BuildConfiguration(),
            resetHistory: true,
            cancellationToken);
    }

    public Task RecordOneSecondAsync(CancellationToken cancellationToken) =>
        recordingWorkspace.RecordOneSecondAsync(
            configurationEditor.BuildConfiguration(),
            cancellationToken);

    public Task ShowLiveTrendAsync(CancellationToken cancellationToken) =>
        trendWorkspace.ShowLiveAsync(
            configurationEditor.BuildConfiguration(),
            cancellationToken);
}
