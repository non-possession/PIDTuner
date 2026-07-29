using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Csv;

public sealed class StablePidSampleCsvExchange : ICsvSampleExchange
{
    private readonly ConfigurablePidSampleCsvExchange _inner = new(PidSampleFieldProfile.CreateDefault());

    public async Task<IReadOnlyList<PidSample>> ImportAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        return await _inner.ImportAsync(csvStream, cancellationToken);
    }

    public async Task ExportAsync(
        IReadOnlyList<PidSample> samples,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await _inner.ExportAsync(samples, destination, cancellationToken);
    }
}
