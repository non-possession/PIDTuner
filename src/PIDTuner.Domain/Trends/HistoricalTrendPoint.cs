namespace PIDTuner.Domain.Trends;

public sealed record HistoricalTrendPoint(
    DateTimeOffset Timestamp,
    double Value,
    string Quality,
    string Source);
