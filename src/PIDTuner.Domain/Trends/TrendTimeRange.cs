namespace PIDTuner.Domain.Trends;

public sealed record TrendTimeRange(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Duration => End - Start;
}
