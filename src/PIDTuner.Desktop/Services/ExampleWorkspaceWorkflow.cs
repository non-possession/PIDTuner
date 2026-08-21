using System.IO;
using PIDTuner.Desktop.ViewModels;

namespace PIDTuner.Desktop.Services;

public sealed class ExampleWorkspaceWorkflow(
    string repositoryRoot,
    FieldProfileEditorViewModel fieldProfileEditor,
    OfflineAnalysisViewModel offlineAnalysis)
{
    public async Task<WorkspaceOperationResult> LoadAsync(CancellationToken cancellationToken)
    {
        var fieldProfilePath = Path.Combine(repositoryRoot, "config", "pid-sample-fields.example.json");
        var csvPath = Path.Combine(repositoryRoot, "samples", "offline-step-response.csv");
        if (!File.Exists(fieldProfilePath) || !File.Exists(csvPath))
        {
            return WorkspaceOperationResult.Error(
                "示例加载失败",
                "示例文件不存在，请确认从仓库根目录运行程序。");
        }

        try
        {
            await fieldProfileEditor.LoadFromFileAsync(fieldProfilePath, cancellationToken);
            var result = await offlineAnalysis.AnalyzeCsvFileAsync(
                csvPath,
                fieldProfileEditor.Profile,
                cancellationToken);
            return WorkspaceOperationResult.Success(
                "离线分析已完成",
                $"{result.SourceFileName}：读取 {result.SampleCount} 条样本");
        }
        catch (Exception exception)
        {
            return WorkspaceOperationResult.Error("示例加载失败", exception.Message);
        }
    }
}
