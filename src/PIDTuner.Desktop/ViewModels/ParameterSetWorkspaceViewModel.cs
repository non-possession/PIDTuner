namespace PIDTuner.Desktop.ViewModels;

public sealed class ParameterSetWorkspaceViewModel(
    ParameterSetLibraryViewModel library,
    OfflineAnalysisViewModel analysis)
{
    public async Task<ParameterSetWorkspaceOperationResult> SaveLatestAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await library.SaveAsync(
                analysis.LastSamples,
                analysis.LastTestSessionId == Guid.Empty ? null : analysis.LastTestSessionId,
                analysis.LastSourceFileName,
                cancellationToken);
            return new(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            return ParameterSetWorkspaceOperationResult.Error("参数方案保存失败", exception.Message);
        }
    }

    public async Task<ParameterSetWorkspaceOperationResult?> LoadAsync(
        bool notifySuccess,
        CancellationToken cancellationToken)
    {
        try
        {
            await library.LoadAsync(cancellationToken);
            return notifySuccess
                ? ParameterSetWorkspaceOperationResult.Info("参数方案已刷新", library.Status)
                : null;
        }
        catch (Exception exception)
        {
            library.MarkLoadFailed();
            return ParameterSetWorkspaceOperationResult.Error("参数方案加载失败", exception.Message);
        }
    }
}

public sealed record ParameterSetWorkspaceOperationResult(string Title, string Message, string Kind)
{
    public static ParameterSetWorkspaceOperationResult Info(string title, string message) =>
        new(title, message, "Info");

    public static ParameterSetWorkspaceOperationResult Error(string title, string message) =>
        new(title, message, "Error");
}
