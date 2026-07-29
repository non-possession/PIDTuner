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
        var peakSample = responseSpan >= 0
            ? selected.MaxBy(sample => sample.ProcessValue!.Value)!
            : selected.MinBy(sample => sample.ProcessValue!.Value)!;
        var minimumProcessValue = selected.Min(sample => sample.ProcessValue!.Value);
        var errorMetrics = CalculateErrorMetrics(selected);
        var outputStandardDeviation = CalculateOutputStandardDeviation(selected);
        var hasSustainedOscillation = DetectSustainedOscillation(selected, target);
        var hasOutputSaturation = DetectOutputSaturation(selected);

        if (Math.Abs(responseSpan) < double.Epsilon)
        {
            return new PidResponseMetrics(
                null,
                null,
                null,
                Math.Abs(target - final),
                peakSample.ProcessValue!.Value,
                peakSample.Timestamp - first.Timestamp,
                minimumProcessValue,
                errorMetrics.MeanAbsoluteError,
                errorMetrics.MeanSquaredError,
                errorMetrics.IntegralAbsoluteError,
                outputStandardDeviation,
                hasSustainedOscillation,
                hasOutputSaturation);
        }

        var overshoot = CalculateOvershootPercent(selected, target, responseSpan);
        var riseTime = CalculateRiseTime(selected, first.Timestamp, initial, responseSpan);
        var settlingTime = CalculateSettlingTime(selected, first.Timestamp, target, Math.Abs(responseSpan));
        var steadyStateError = Math.Abs(target - final);

        return new PidResponseMetrics(
            overshoot,
            riseTime,
            settlingTime,
            steadyStateError,
            peakSample.ProcessValue!.Value,
            peakSample.Timestamp - first.Timestamp,
            minimumProcessValue,
            errorMetrics.MeanAbsoluteError,
            errorMetrics.MeanSquaredError,
            errorMetrics.IntegralAbsoluteError,
            outputStandardDeviation,
            hasSustainedOscillation,
            hasOutputSaturation);
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

    private static (double MeanAbsoluteError, double MeanSquaredError, double IntegralAbsoluteError) CalculateErrorMetrics(
        IReadOnlyList<PidSample> samples)
    {
        var absoluteErrors = samples
            .Select(sample => Math.Abs(sample.SetPoint!.Value - sample.ProcessValue!.Value))
            .ToArray();
        var squaredErrors = absoluteErrors.Select(error => error * error).ToArray();

        return (
            absoluteErrors.Average(),
            squaredErrors.Average(),
            Integrate(samples, absoluteErrors));
    }

    private static double? CalculateOutputStandardDeviation(IReadOnlyList<PidSample> samples)
    {
        var values = samples
            .Where(sample => sample.ManipulatedValue.HasValue)
            .Select(sample => sample.ManipulatedValue!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        var average = values.Average();
        var variance = values.Average(value => Math.Pow(value - average, 2));
        return Math.Sqrt(variance);
    }

    private static double Integrate(IReadOnlyList<PidSample> samples, IReadOnlyList<double> values)
    {
        if (samples.Count < 2)
        {
            return 0;
        }

        double integral = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            var seconds = (samples[index].Timestamp - samples[index - 1].Timestamp).TotalSeconds;
            integral += (values[index - 1] + values[index]) / 2 * seconds;
        }

        return integral;
    }

    private static bool DetectSustainedOscillation(IReadOnlyList<PidSample> samples, double target)
    {
        if (samples.Count < 6)
        {
            return false;
        }

        var signs = samples
            .Select(sample => Math.Sign(sample.ProcessValue!.Value - target))
            .Where(sign => sign != 0)
            .ToArray();

        var crossings = 0;
        for (var index = 1; index < signs.Length; index++)
        {
            if (signs[index] != signs[index - 1])
            {
                crossings++;
            }
        }

        return crossings >= 3;
    }

    private static bool DetectOutputSaturation(IReadOnlyList<PidSample> samples)
    {
        var values = samples
            .Where(sample => sample.ManipulatedValue.HasValue)
            .Select(sample => sample.ManipulatedValue!.Value)
            .ToArray();

        if (values.Length < 4)
        {
            return false;
        }

        var min = values.Min();
        var max = values.Max();
        var span = Math.Max(1, Math.Abs(max - min));
        var tolerance = span * 0.02;
        var nearLimitCount = values.Count(value => Math.Abs(value - min) <= tolerance || Math.Abs(value - max) <= tolerance);

        return nearLimitCount >= values.Length * 0.4;
    }
}
