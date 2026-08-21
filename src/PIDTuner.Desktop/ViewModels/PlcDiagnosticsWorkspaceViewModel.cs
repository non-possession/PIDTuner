using PIDTuner.Desktop.Services;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcDiagnosticsWorkspaceViewModel
{
    private readonly PlcDebugViewModel _debug;
    private readonly PlcLiveMonitorViewModel _liveMonitor;
    private readonly PlcConfigurationEditorViewModel _configurationEditor;
    private readonly PlcDiagnosticsController _controller;

    public PlcDiagnosticsWorkspaceViewModel(
        PlcDebugViewModel debug,
        PlcLiveMonitorViewModel liveMonitor,
        PlcConfigurationEditorViewModel configurationEditor)
    {
        _debug = debug;
        _liveMonitor = liveMonitor;
        _configurationEditor = configurationEditor;
        _controller = new PlcDiagnosticsController(debug, ApplyOperation);
    }

    public event Action<WorkspaceOperationResult>? NotificationRequested;

    public async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (_debug.IsDiagnosticsRunning)
        {
            await StopAsync("诊断由用户手动停止。", cancellationToken);
            return;
        }

        if (!_liveMonitor.IsMonitoring)
        {
            Notify(WorkspaceOperationResult.Warning("无法启动实时诊断", "请先启动实时监控。"));
            return;
        }

        await _controller.StartAsync(
            _configurationEditor.BuildConfiguration(),
            TimeSpan.FromMinutes(_debug.DiagnosticsDurationMinutes),
            cancellationToken);
    }

    public Task StopAsync(string reason, CancellationToken cancellationToken) =>
        _controller.StopAsync(reason, cancellationToken);

    private void ApplyOperation(PlcDiagnosticsOperationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.NotificationTitle)
            && !string.IsNullOrWhiteSpace(result.NotificationMessage)
            && !string.IsNullOrWhiteSpace(result.NotificationKind))
        {
            Notify(new WorkspaceOperationResult(
                result.NotificationTitle,
                result.NotificationMessage,
                result.NotificationKind));
        }
    }

    private void Notify(WorkspaceOperationResult result) =>
        NotificationRequested?.Invoke(result);
}
