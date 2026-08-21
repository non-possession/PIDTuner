namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcConnectionWorkspaceViewModel(
    PlcConfigurationEditorViewModel configurationEditor,
    PlcLiveMonitorViewModel liveMonitor,
    PlcLiveWorkspaceViewModel liveWorkspace)
{
    public async Task<WorkspaceOperationResult> SaveAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var savedPath = await configurationEditor.SaveToFileAsync(fileName, cancellationToken);
            return WorkspaceOperationResult.Success("PLC 配置已保存", savedPath);
        }
        catch (Exception exception)
        {
            return WorkspaceOperationResult.Error("PLC 配置保存失败", exception.Message);
        }
    }

    public async Task<WorkspaceOperationResult> CheckCommunicationAsync(
        bool startMonitoringOnSuccess,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = configurationEditor.BuildConfiguration();
            var result = await configurationEditor.CheckCommunicationAsync(cancellationToken);
            if (result.IsReachable && startMonitoringOnSuccess)
            {
                await liveWorkspace.StartAsync(configuration, resetHistory: true, cancellationToken);
            }

            return new WorkspaceOperationResult(
                result.Title,
                configurationEditor.CommunicationStatus,
                result.Kind);
        }
        catch (Exception exception)
        {
            return WorkspaceOperationResult.Error("PLC 通信检查失败", exception.Message);
        }
    }

    public async Task<WorkspaceOperationResult> LoadAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            await configurationEditor.LoadFromFileAsync(fileName, cancellationToken);
            liveMonitor.ClearTags();
            liveMonitor.MonitorStatus = "PLC 配置已更新，等待刷新点位。";
            return await CheckCommunicationAsync(startMonitoringOnSuccess: true, cancellationToken);
        }
        catch (Exception exception)
        {
            return WorkspaceOperationResult.Error("PLC 配置加载失败", exception.Message);
        }
    }
}
