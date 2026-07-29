using PIDTuner.Domain.Analysis;

namespace PIDTuner.Application.Interfaces;

public interface IPidRecommendationReviewRepository
{
    Task SaveAsync(PidRecommendationReview review, CancellationToken cancellationToken);

    Task<IReadOnlyList<PidRecommendationReview>> ListAsync(CancellationToken cancellationToken);
}
