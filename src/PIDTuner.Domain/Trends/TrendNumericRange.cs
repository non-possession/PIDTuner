namespace PIDTuner.Domain.Trends;

public sealed record TrendNumericRange(double Minimum, double Maximum)
{
    public double Span => Maximum - Minimum;
}
