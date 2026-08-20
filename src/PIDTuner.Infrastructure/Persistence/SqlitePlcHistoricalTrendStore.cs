using System.Globalization;
using Microsoft.Data.Sqlite;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class SqlitePlcHistoricalTrendStore : IPlcHistoricalTrendStore
{
    private const int DefaultMaximumPointsPerTag = 20_000;
    private readonly SqlitePlcLiveDiagnosticsStore _writer;

    public SqlitePlcHistoricalTrendStore(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _writer = new SqlitePlcLiveDiagnosticsStore(DatabasePath);
    }

    public string DatabasePath { get; }

    public async Task<IPlcHistoricalTrendWriteSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var session = await _writer.StartSessionAsync(
            configuration,
            TimeSpan.FromDays(3650),
            cancellationToken);
        return new WriteSession(session);
    }

    public async Task<(DateTimeOffset Start, DateTimeOffset End)?> GetAvailableRangeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(DatabasePath))
        {
            return null;
        }

        await using var connection = OpenReadConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MIN(planned_timestamp_utc), MAX(planned_timestamp_utc) FROM plc_sample_frames;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1))
        {
            return null;
        }

        return (ParseTimestamp(reader.GetString(0)), ParseTimestamp(reader.GetString(1)));
    }

    public async Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> QueryFramesAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumPointsPerTag,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(DatabasePath))
        {
            return Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        maximumPointsPerTag = Math.Max(2, maximumPointsPerTag <= 0 ? DefaultMaximumPointsPerTag : maximumPointsPerTag);
        await using var connection = OpenReadConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH samples AS (
                SELECT
                    v.tag_id,
                    v.tag_name,
                    v.address,
                    v.value,
                    v.unit,
                    f.planned_timestamp_utc AS timestamp_utc,
                    v.quality,
                    v.source,
                    ROW_NUMBER() OVER (PARTITION BY v.tag_id ORDER BY f.planned_timestamp_utc) AS row_number,
                    COUNT(*) OVER (PARTITION BY v.tag_id) AS row_count
                FROM plc_sample_values v
                INNER JOIN plc_sample_frames f
                    ON f.session_id = v.session_id AND f.frame_index = v.frame_index
                WHERE f.planned_timestamp_utc >= $start AND f.planned_timestamp_utc <= $end
            )
            SELECT tag_id, tag_name, address, value, unit, timestamp_utc, quality, source
            FROM samples
            WHERE row_count <= $maximum_points
               OR row_number = 1
               OR row_number = row_count
               OR (row_number % ((row_count + $maximum_points - 1) / $maximum_points)) = 0
            ORDER BY timestamp_utc, tag_id;
            """;
        command.Parameters.AddWithValue("$start", FormatTimestamp(start));
        command.Parameters.AddWithValue("$end", FormatTimestamp(end));
        command.Parameters.AddWithValue("$maximum_points", maximumPointsPerTag);

        var frames = new SortedDictionary<DateTimeOffset, List<PlcTagSnapshot>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var timestamp = ParseTimestamp(reader.GetString(5));
            if (!frames.TryGetValue(timestamp, out var frame))
            {
                frame = [];
                frames[timestamp] = frame;
            }

            frame.Add(new PlcTagSnapshot(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                timestamp,
                reader.GetString(6),
                reader.GetString(7)));
        }

        return frames.Values.Select(frame => (IReadOnlyList<PlcTagSnapshot>)frame).ToArray();
    }

    private SqliteConnection OpenReadConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };
        return new SqliteConnection(builder.ToString());
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed class WriteSession(IPlcLiveDiagnosticsSession session) : IPlcHistoricalTrendWriteSession
    {
        public string DatabasePath => session.DatabasePath;

        public void Enqueue(PlcAcquisitionFrame frame) => session.Enqueue(frame);

        public async Task<PlcHistoricalTrendWriteSummary> StopAsync(CancellationToken cancellationToken)
        {
            var summary = await session.StopAsync(cancellationToken);
            return new PlcHistoricalTrendWriteSummary(
                summary.DatabasePath,
                summary.FrameCount,
                summary.SnapshotCount);
        }

        public async ValueTask DisposeAsync()
        {
            await session.DisposeAsync();
        }
    }
}
