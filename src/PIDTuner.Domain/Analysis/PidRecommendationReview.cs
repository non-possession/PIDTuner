namespace PIDTuner.Domain.Analysis;

public sealed record PidRecommendationReview(
    Guid Id,
    Guid? TestSessionId,
    string SourceName,
    string Parameter,
    PidTuningAdjustmentDirection Direction,
    string Adjustment,
    PidRecommendationReviewDecision Decision,
    string EngineerNote,
    DateTimeOffset CreatedAt);
