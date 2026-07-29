using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface ICsvSampleExchange
{
    Task<IReadOnlyList<PidSample>> ImportAsync(Stream csvStream, CancellationToken cancellationToken);

    Task ExportAsync(IReadOnlyList<PidSample> samples, Stream destination, CancellationToken cancellationToken);
}
