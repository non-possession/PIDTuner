using System.Globalization;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PidRecommendationReviewViewModel(PidRecommendationReview review)
{
    public string CreatedAt { get; } = review.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string SourceName { get; } = review.SourceName;

    public string Parameter { get; } = review.Parameter;

    public string Direction { get; } = review.Direction.ToString();

    public string Adjustment { get; } = review.Adjustment;

    public string Decision { get; } = review.Decision.ToString();

    public string EngineerNote { get; } = string.IsNullOrWhiteSpace(review.EngineerNote) ? "-" : review.EngineerNote;
}
