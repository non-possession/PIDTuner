namespace PIDTuner.Desktop.ViewModels;

public sealed class HistoryComparisonMetricViewModel(
    string metric,
    string baseline,
    string candidate,
    string delta)
{
    public string Metric { get; } = metric;

    public string Baseline { get; } = baseline;

    public string Candidate { get; } = candidate;

    public string Delta { get; } = delta;
}
