using System.Text.Json;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class JsonPidRecommendationReviewRepository(string storageDirectory) : IPidRecommendationReviewRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(storageDirectory, "recommendation-reviews.json");

    public async Task SaveAsync(PidRecommendationReview review, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var reviews = (await ListAsync(cancellationToken)).ToList();
        var existingIndex = reviews.FindIndex(item => item.Id == review.Id);

        if (existingIndex >= 0)
        {
            reviews[existingIndex] = review;
        }
        else
        {
            reviews.Add(review);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, reviews, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<PidRecommendationReview>> ListAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<PidRecommendationReview>();
        }

        await using var stream = File.OpenRead(_filePath);
        var reviews = await JsonSerializer.DeserializeAsync<List<PidRecommendationReview>>(stream, JsonOptions, cancellationToken);

        if (reviews is null)
        {
            return Array.Empty<PidRecommendationReview>();
        }

        return reviews;
    }
}
