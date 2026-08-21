using PIDTuner.Desktop.Services;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcTrendExportViewModel(PlcTrendVisibleExportWorkflow workflow)
{
    public WorkspaceOperationResult? ValidateVisibleExport(PlcTrendVisibleExport export) =>
        export.Points.Count == 0
            ? WorkspaceOperationResult.Warning(
                "无法导出可见趋势",
                "当前趋势画布没有可导出的可见数据点。")
            : null;

    public Task<PlcTrendVisibleExportResult> ExportVisibleAsync(
        string fileName,
        PlcTrendVisibleExport export,
        CancellationToken cancellationToken) =>
        workflow.ExportAsync(fileName, export, cancellationToken);

    public async Task<WorkspaceOperationResult> ExportVisibleResultAsync(
        string fileName,
        PlcTrendVisibleExport export,
        CancellationToken cancellationToken)
    {
        var validation = ValidateVisibleExport(export);
        if (validation is not null)
        {
            return validation;
        }

        try
        {
            var result = await ExportVisibleAsync(fileName, export, cancellationToken);
            return WorkspaceOperationResult.Success(
                "可见趋势已导出",
                string.Join(
                    Environment.NewLine,
                    $"行数：{result.PointCount}",
                    $"范围：{result.VisibleStart:yyyy-MM-dd HH:mm:ss.fff} - {result.VisibleEnd:yyyy-MM-dd HH:mm:ss.fff}",
                    $"路径：{result.AbsolutePath}"));
        }
        catch (Exception exception)
        {
            return WorkspaceOperationResult.Error("可见趋势导出失败", exception.Message);
        }
    }
}
