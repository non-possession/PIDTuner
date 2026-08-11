using PIDTuner.Domain.Plc;
using PIDTuner.Domain.Trends;

namespace PIDTuner.Application.Services;

public sealed class PlcTrendDatasetBridge
{
    public HistoricalTrendDataset BuildDataset(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        var series = frames
            .SelectMany(frame => frame)
            .Where(snapshot => snapshot.Value is double value && !double.IsNaN(value) && !double.IsInfinity(value))
            .GroupBy(snapshot => snapshot.TagId)
            .Select(group =>
            {
                var ordered = group
                    .OrderBy(snapshot => snapshot.Timestamp)
                    .ToArray();
                var first = ordered[0];
                return new HistoricalTrendSeries(
                    first.TagId,
                    first.Name,
                    first.Address,
                    first.Unit,
                    ordered
                        .Select(snapshot => new HistoricalTrendPoint(
                            snapshot.Timestamp,
                            (double)snapshot.Value!,
                            snapshot.Quality,
                            snapshot.Source))
                        .ToArray());
            })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new HistoricalTrendDataset(series);
    }
}
