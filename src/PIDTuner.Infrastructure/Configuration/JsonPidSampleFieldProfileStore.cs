using System.Text.Json;
using System.Text.Json.Serialization;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Infrastructure.Configuration;

public sealed class JsonPidSampleFieldProfileStore : IPidSampleFieldProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PidSampleFieldProfile> LoadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var profile = await JsonSerializer.DeserializeAsync<PidSampleFieldProfile>(
            stream,
            SerializerOptions,
            cancellationToken);

        return Validate(profile);
    }

    public async Task SaveAsync(
        PidSampleFieldProfile profile,
        Stream stream,
        CancellationToken cancellationToken)
    {
        Validate(profile);
        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions, cancellationToken);
    }

    private static PidSampleFieldProfile Validate(PidSampleFieldProfile? profile)
    {
        if (profile is null)
        {
            throw new FormatException("PID sample field profile is empty.");
        }

        if (profile.SchemaVersion != 1)
        {
            throw new FormatException($"Unsupported PID sample field profile schema version: {profile.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(profile.ProfileName))
        {
            throw new FormatException("PID sample field profile name is required.");
        }

        if (profile.Fields.Count == 0)
        {
            throw new FormatException("PID sample field profile must contain at least one field.");
        }

        var duplicateKey = profile.Fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateKey is not null)
        {
            throw new FormatException($"Duplicate PID sample field key: {duplicateKey.Key}.");
        }

        return profile;
    }
}
