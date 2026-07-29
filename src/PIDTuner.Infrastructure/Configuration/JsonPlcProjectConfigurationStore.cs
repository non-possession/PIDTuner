using System.Text.Json;
using System.Text.Json.Serialization;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Infrastructure.Configuration;

public sealed class JsonPlcProjectConfigurationStore : IPlcProjectConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<PlcProjectConfiguration> LoadAsync(Stream stream, CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<PlcProjectConfiguration>(stream, JsonOptions, cancellationToken)
            ?? throw new FormatException("PLC project configuration JSON is empty.");
    }

    public async Task SaveAsync(
        PlcProjectConfiguration configuration,
        Stream stream,
        CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
    }
}
