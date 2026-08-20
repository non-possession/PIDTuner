using System.IO;
using PIDTuner.Domain.Analysis;
using PIDTuner.Infrastructure.Csv;

namespace PIDTuner.Desktop.Services;

public sealed class AnalysisResultExportWorkflow
{
    private readonly PidAnalysisResultCsvExporter _exporter = new();

    public Task ExportAsync(
        string fileName,
        AnalysisWindow window,
        PidResponseMetrics metrics,
        PidResponseAssessment assessment,
        CancellationToken cancellationToken) =>
        ExportToFileAsync(fileName, window, metrics, assessment, cancellationToken);

    private async Task ExportToFileAsync(
        string fileName,
        AnalysisWindow window,
        PidResponseMetrics metrics,
        PidResponseAssessment assessment,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(fileName);
        await _exporter.ExportAsync(window, metrics, assessment, stream, cancellationToken);
    }
}
