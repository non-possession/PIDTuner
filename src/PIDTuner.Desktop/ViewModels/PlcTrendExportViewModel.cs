using PIDTuner.Desktop.Services;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcTrendExportViewModel(PlcTrendVisibleExportWorkflow workflow)
{
    public Task<PlcTrendVisibleExportResult> ExportVisibleAsync(
        string fileName,
        PlcTrendVisibleExport export,
        CancellationToken cancellationToken) =>
        workflow.ExportAsync(fileName, export, cancellationToken);
}
