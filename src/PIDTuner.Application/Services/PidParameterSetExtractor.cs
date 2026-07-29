using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Services;

public sealed class PidParameterSetExtractor
{
    public PidParameterSet? Extract(
        IReadOnlyList<PidSample> samples,
        Guid? testSessionId,
        string sourceName,
        string? notes = null)
    {
        var sample = samples
            .Where(item => item.Kp.HasValue || item.KiOrTi.HasValue || item.KdOrTd.HasValue)
            .OrderByDescending(item => item.Timestamp)
            .FirstOrDefault();

        if (sample is null)
        {
            return null;
        }

        return new PidParameterSet(
            sample.ParameterSetId ?? Guid.NewGuid(),
            testSessionId,
            string.IsNullOrWhiteSpace(sourceName) ? "pid-parameter-set" : sourceName,
            sample.Kp,
            sample.KiOrTi,
            sample.KdOrTd,
            sample.Timestamp,
            sourceName,
            notes);
    }
}
