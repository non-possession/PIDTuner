using System.Text.Json;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class JsonTestSessionRepository(string storageDirectory) : ITestSessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _filePath = Path.Combine(storageDirectory, "test-sessions.json");

    public async Task SaveAsync(TestSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var sessions = (await ListAsync(cancellationToken)).ToList();
        var existingIndex = sessions.FindIndex(item => item.Id == session.Id);

        if (existingIndex >= 0)
        {
            sessions[existingIndex] = session;
        }
        else
        {
            sessions.Add(session);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, sessions, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<TestSession>> ListAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<TestSession>();
        }

        await using var stream = File.OpenRead(_filePath);
        var sessions = await JsonSerializer.DeserializeAsync<List<TestSession>>(stream, JsonOptions, cancellationToken);
        if (sessions is null)
        {
            return Array.Empty<TestSession>();
        }

        return sessions;
    }
}
