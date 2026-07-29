namespace PIDTuner.Domain.Analysis;

public sealed record PidTuningRecommendation(
    string Parameter,
    PidTuningAdjustmentDirection Direction,
    string Adjustment,
    string Reason,
    string ExpectedEffect,
    string Risk,
    double Confidence);
