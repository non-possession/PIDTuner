using PIDTuner.Application.DTOs;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Application.UseCases;

public sealed class AnalyzeOfflineCsvUseCase(
    ICsvSampleExchange csvSampleExchange,
    IPidAnalysisService pidAnalysisService)
{
    public async Task<OfflineAnalysisResult> AnalyzeAsync(
        Stream csvStream,
        AnalysisWindow? requestedWindow,
        CancellationToken cancellationToken)
    {
        var samples = await csvSampleExchange.ImportAsync(csvStream, cancellationToken);
        var window = requestedWindow ?? CreateFullWindow(samples);
        var metrics = pidAnalysisService.Analyze(samples, window);

        return new OfflineAnalysisResult(samples, window, metrics);
    }

    private static AnalysisWindow CreateFullWindow(IReadOnlyList<Domain.Models.PidSample> samples)
    {
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("Cannot analyze an empty PID sample set.");
        }

        return new AnalysisWindow(
            samples.Min(sample => sample.Timestamp),
            samples.Max(sample => sample.Timestamp));
    }
}
