using System.Text.Json;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class JsonPidParameterSetRepository(string storageDirectory) : IPidParameterSetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(storageDirectory, "pid-parameter-sets.json");

    public async Task SaveAsync(PidParameterSet parameterSet, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var parameterSets = (await ListAsync(cancellationToken)).ToList();
        var existingIndex = parameterSets.FindIndex(item => item.Id == parameterSet.Id);

        if (existingIndex >= 0)
        {
            parameterSets[existingIndex] = parameterSet;
        }
        else
        {
            parameterSets.Add(parameterSet);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, parameterSets, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<PidParameterSet>> ListAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<PidParameterSet>();
        }

        await using var stream = File.OpenRead(_filePath);
        var parameterSets = await JsonSerializer.DeserializeAsync<List<PidParameterSet>>(stream, JsonOptions, cancellationToken);

        if (parameterSets is null)
        {
            return Array.Empty<PidParameterSet>();
        }

        return parameterSets;
    }
}
