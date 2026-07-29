namespace PIDTuner.Domain.Analysis;

public sealed record PidResponseAssessment(
    PidResponseSeverity Severity,
    string Summary,
    IReadOnlyList<string> Findings);
