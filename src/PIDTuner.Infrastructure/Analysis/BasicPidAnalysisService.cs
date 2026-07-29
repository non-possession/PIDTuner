using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Analysis;

public sealed class BasicPidAnalysisService : IPidAnalysisService
{
    private const double SettlingTolerance = 0.02;
    private const double RiseThreshold = 0.9;

    public PidResponseMetrics Analyze(IReadOnlyList<PidSample> samples, AnalysisWindow window)
    {
        var selected = samples
            .Where(sample => window.Contains(sample.Timestamp))
            .Where(sample => sample.SetPoint.HasValue && sample.ProcessValue.HasValue)
            .OrderBy(sample => sample.Timestamp)
            .ToArray();

        if (selected.Length == 0)
        {
            return new PidResponseMetrics(null, null, null, null);
        }

        var first = selected[0];
        var target = first.SetPoint!.Value;
        var initial = first.ProcessValue!.Value;
        var final = selected[^1].ProcessValue!.Value;
        var responseSpan = target - initial;

        if (Math.Abs(responseSpan) < double.Epsilon)
        {
            return new PidResponseMetrics(null, null, null, Math.Abs(target - final));
        }

        var overshoot = CalculateOvershootPercent(selected, target, responseSpan);
        var riseTime = CalculateRiseTime(selected, first.Timestamp, initial, responseSpan);
        var settlingTime = CalculateSettlingTime(selected, first.Timestamp, target, Math.Abs(responseSpan));
        var steadyStateError = Math.Abs(target - final);

        return new PidResponseMetrics(overshoot, riseTime, settlingTime, steadyStateError);
    }

    private static double? CalculateOvershootPercent(IReadOnlyList<PidSample> samples, double target, double responseSpan)
    {
        var furthest = responseSpan > 0
            ? samples.Max(sample => sample.ProcessValue!.Value)
            : samples.Min(sample => sample.ProcessValue!.Value);

        var overshoot = responseSpan > 0
            ? furthest - target
            : target - furthest;

        return overshoot <= 0 ? 0 : overshoot / Math.Abs(responseSpan) * 100;
    }

    private static TimeSpan? CalculateRiseTime(
        IReadOnlyList<PidSample> samples,
        DateTimeOffset startedAt,
        double initial,
        double responseSpan)
    {
        var threshold = initial + responseSpan * RiseThreshold;

        var crossed = responseSpan > 0
            ? samples.FirstOrDefault(sample => sample.ProcessValue!.Value >= threshold)
            : samples.FirstOrDefault(sample => sample.ProcessValue!.Value <= threshold);

        return crossed is null ? null : crossed.Timestamp - startedAt;
    }

    private static TimeSpan? CalculateSettlingTime(
        IReadOnlyList<PidSample> samples,
        DateTimeOffset startedAt,
        double target,
        double responseMagnitude)
    {
        var tolerance = responseMagnitude * SettlingTolerance;

        for (var index = 0; index < samples.Count; index++)
        {
            var remainsSettled = samples
                .Skip(index)
                .All(sample => Math.Abs(sample.ProcessValue!.Value - target) <= tolerance);

            if (remainsSettled)
            {
                return samples[index].Timestamp - startedAt;
            }
        }

        return null;
    }
}
