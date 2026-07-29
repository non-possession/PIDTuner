using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface IDataAcquisitionService
{
    IAsyncEnumerable<PidSample> ReadSamplesAsync(
        IReadOnlyCollection<TagDefinition> tags,
        TimeSpan samplingInterval,
        CancellationToken cancellationToken);
}
