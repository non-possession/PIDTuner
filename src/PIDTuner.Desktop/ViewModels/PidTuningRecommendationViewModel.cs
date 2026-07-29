using System.Globalization;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PidTuningRecommendationViewModel(PidTuningRecommendation recommendation)
{
    public string Parameter { get; } = recommendation.Parameter;

    public string Direction { get; } = recommendation.Direction.ToString();

    public string Adjustment { get; } = recommendation.Adjustment;

    public string Reason { get; } = recommendation.Reason;

    public string ExpectedEffect { get; } = recommendation.ExpectedEffect;

    public string Risk { get; } = recommendation.Risk;

    public string Confidence { get; } = recommendation.Confidence.ToString("P0", CultureInfo.InvariantCulture);
}
