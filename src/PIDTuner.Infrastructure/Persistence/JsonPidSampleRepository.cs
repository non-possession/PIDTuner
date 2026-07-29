using System.Text.Json;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class JsonPidSampleRepository(string storageDirectory) : IPidSampleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task SaveBatchAsync(IReadOnlyCollection<PidSample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        var bySession = samples.GroupBy(sample => sample.TestSessionId);
        Directory.CreateDirectory(storageDirectory);

        foreach (var sessionSamples in bySession)
        {
            var filePath = GetSessionFilePath(sessionSamples.Key);
            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, sessionSamples.ToArray(), JsonOptions, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PidSample>> GetBySessionAsync(Guid testSessionId, CancellationToken cancellationToken)
    {
        var filePath = GetSessionFilePath(testSessionId);
        if (!File.Exists(filePath))
        {
            return Array.Empty<PidSample>();
        }

        await using var stream = File.OpenRead(filePath);
        var samples = await JsonSerializer.DeserializeAsync<List<PidSample>>(stream, JsonOptions, cancellationToken);
        if (samples is null)
        {
            return Array.Empty<PidSample>();
        }

        return samples;
    }

    private string GetSessionFilePath(Guid testSessionId)
    {
        return Path.Combine(storageDirectory, $"{testSessionId:D}.samples.json");
    }
}
