using PIDTuner.Domain.Models;

namespace PIDTuner.Domain.Trends;

public sealed class PidTrendSeriesBuilder
{
    public PidTrendSeries Build(IReadOnlyList<PidSample> samples)
    {
        var ordered = samples
            .OrderBy(sample => sample.Timestamp)
            .ToArray();

        var startedAt = ordered.FirstOrDefault()?.Timestamp ?? DateTimeOffset.MinValue;
        var endedAt = ordered.LastOrDefault()?.Timestamp ?? startedAt;
        var elapsedTicks = Math.Max(1, (endedAt - startedAt).Ticks);
        var scale = CreateValueScale(ordered);

        return new PidTrendSeries(
            BuildSeries("sp", "SP", ordered, sample => sample.SetPoint, startedAt, elapsedTicks, scale),
            BuildSeries("pv", "PV", ordered, sample => sample.ProcessValue, startedAt, elapsedTicks, scale),
            BuildSeries("mv", "MV", ordered, sample => sample.ManipulatedValue, startedAt, elapsedTicks, scale));
    }

    private static TrendSeries BuildSeries(
        string key,
        string displayName,
        IReadOnlyList<PidSample> samples,
        Func<PidSample, double?> valueSelector,
        DateTimeOffset startedAt,
        long elapsedTicks,
        ValueScale scale)
    {
        var points = samples
            .Where(sample => valueSelector(sample).HasValue)
            .Select(sample =>
            {
                var value = valueSelector(sample)!.Value;
                return new TrendPoint(
                    sample.Timestamp,
                    value,
                    (double)(sample.Timestamp - startedAt).Ticks / elapsedTicks,
                    scale.Normalize(value));
            })
            .ToArray();

        return new TrendSeries(key, displayName, points);
    }

    private static ValueScale CreateValueScale(IReadOnlyList<PidSample> samples)
    {
        var values = samples
            .SelectMany(sample => new[] { sample.SetPoint, sample.ProcessValue, sample.ManipulatedValue })
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return new ValueScale(0, 1);
        }

        var minimum = values.Min();
        var maximum = values.Max();

        return Math.Abs(maximum - minimum) < double.Epsilon
            ? new ValueScale(minimum - 1, maximum + 1)
            : new ValueScale(minimum, maximum);
    }

    private sealed record ValueScale(double Minimum, double Maximum)
    {
        public double Normalize(double value)
        {
            return (value - Minimum) / (Maximum - Minimum);
        }
    }
}
