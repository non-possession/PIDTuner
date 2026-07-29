namespace PIDTuner.Domain.Trends;

public sealed record TrendPoint(
    DateTimeOffset Timestamp,
    double Value,
    double NormalizedX,
    double NormalizedY);
