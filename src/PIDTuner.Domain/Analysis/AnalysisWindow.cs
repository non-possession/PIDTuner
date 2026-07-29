namespace PIDTuner.Domain.Analysis;

public sealed record AnalysisWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset timestamp) => timestamp >= Start && timestamp <= End;
}
