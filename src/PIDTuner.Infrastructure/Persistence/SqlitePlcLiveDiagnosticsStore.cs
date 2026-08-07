using System.Globalization;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class SqlitePlcLiveDiagnosticsStore(string databasePath) : IPlcLiveDiagnosticsStore
{
    private const int MaxQueuedFrames = 10_000;

    public async Task<IPlcLiveDiagnosticsSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Diagnostic duration must be greater than zero.");
        }

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var sessionId = Guid.NewGuid();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var endsAtUtc = startedAtUtc.Add(duration);

        await using var connection = OpenConnection(fullPath);
        await EnsureSchemaAsync(connection, cancellationToken);
        await InsertSessionAsync(connection, sessionId, configuration, startedAtUtc, endsAtUtc, cancellationToken);

        return new SqlitePlcLiveDiagnosticsSession(
            fullPath,
            sessionId,
            startedAtUtc,
            endsAtUtc);
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS plc_diagnostic_sessions (
                session_id TEXT PRIMARY KEY,
                configuration_name TEXT NOT NULL,
                protocol TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                ends_at_utc TEXT NOT NULL,
                stopped_at_utc TEXT NULL,
                default_sampling_ms INTEGER NOT NULL,
                minimum_sampling_ms INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS plc_sample_frames (
                session_id TEXT NOT NULL,
                frame_index INTEGER NOT NULL,
                planned_timestamp_utc TEXT NOT NULL,
                request_started_timestamp_utc TEXT NOT NULL,
                response_received_timestamp_utc TEXT NOT NULL,
                buffered_timestamp_utc TEXT NOT NULL,
                ui_presented_timestamp_utc TEXT NOT NULL,
                schedule_delay_ms REAL NOT NULL,
                read_duration_ms REAL NOT NULL,
                buffer_delay_ms REAL NOT NULL,
                ui_delay_ms REAL NOT NULL,
                snapshot_count INTEGER NOT NULL,
                state INTEGER NOT NULL,
                PRIMARY KEY (session_id, frame_index)
            );

            CREATE TABLE IF NOT EXISTS plc_sample_values (
                session_id TEXT NOT NULL,
                frame_index INTEGER NOT NULL,
                tag_id TEXT NOT NULL,
                tag_name TEXT NOT NULL,
                address TEXT NOT NULL,
                value REAL NULL,
                unit TEXT NULL,
                timestamp_utc TEXT NOT NULL,
                quality TEXT NOT NULL,
                source TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_plc_sample_values_session_time
                ON plc_sample_values(session_id, timestamp_utc);

            CREATE INDEX IF NOT EXISTS idx_plc_sample_frames_session_time
                ON plc_sample_frames(session_id, planned_timestamp_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSessionAsync(
        SqliteConnection connection,
        Guid sessionId,
        PlcProjectConfiguration configuration,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endsAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO plc_diagnostic_sessions (
                session_id,
                configuration_name,
                protocol,
                ip_address,
                started_at_utc,
                ends_at_utc,
                default_sampling_ms,
                minimum_sampling_ms)
            VALUES (
                $session_id,
                $configuration_name,
                $protocol,
                $ip_address,
                $started_at_utc,
                $ends_at_utc,
                $default_sampling_ms,
                $minimum_sampling_ms);
            """;
        command.Parameters.AddWithValue("$session_id", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$configuration_name", configuration.Name);
        command.Parameters.AddWithValue("$protocol", configuration.Protocol);
        command.Parameters.AddWithValue("$ip_address", configuration.IpAddress);
        command.Parameters.AddWithValue("$started_at_utc", FormatTimestamp(startedAtUtc));
        command.Parameters.AddWithValue("$ends_at_utc", FormatTimestamp(endsAtUtc));
        command.Parameters.AddWithValue("$default_sampling_ms", configuration.DefaultSamplingMilliseconds);
        command.Parameters.AddWithValue("$minimum_sampling_ms", configuration.MinimumSamplingMilliseconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed class SqlitePlcLiveDiagnosticsSession : IPlcLiveDiagnosticsSession
    {
        private readonly Channel<PlcAcquisitionFrame> _frames = Channel.CreateBounded<PlcAcquisitionFrame>(
            new BoundedChannelOptions(MaxQueuedFrames)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
        private readonly CancellationTokenSource _cancellation = new();
        private readonly Task _writerTask;
        private int _stopped;

        public SqlitePlcLiveDiagnosticsSession(
            string databasePath,
            Guid sessionId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endsAtUtc)
        {
            DatabasePath = databasePath;
            SessionId = sessionId;
            StartedAtUtc = startedAtUtc;
            EndsAtUtc = endsAtUtc;
            _writerTask = Task.Run(() => WriteFramesAsync(_cancellation.Token));
        }

        public Guid SessionId { get; }

        public string DatabasePath { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public DateTimeOffset EndsAtUtc { get; }

        public void Enqueue(PlcAcquisitionFrame frame)
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                return;
            }

            _frames.Writer.TryWrite(frame);
        }

        public async Task<PlcLiveDiagnosticsSummary> StopAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
            {
                _frames.Writer.TryComplete();
            }

            try
            {
                await _writerTask.WaitAsync(cancellationToken);
            }
            finally
            {
                _cancellation.Dispose();
            }

            await using var connection = OpenConnection(DatabasePath);
            await MarkStoppedAsync(connection, cancellationToken);
            return await QuerySummaryAsync(connection, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync(CancellationToken.None);
        }

        private async Task WriteFramesAsync(CancellationToken cancellationToken)
        {
            await using var connection = OpenConnection(DatabasePath);
            var batch = new List<PlcAcquisitionFrame>(128);

            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                batch.Add(frame);
                if (batch.Count >= 128)
                {
                    await InsertBatchAsync(connection, batch, cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await InsertBatchAsync(connection, batch, cancellationToken);
            }
        }

        private async Task InsertBatchAsync(
            SqliteConnection connection,
            IReadOnlyList<PlcAcquisitionFrame> frames,
            CancellationToken cancellationToken)
        {
            await using var transaction = connection.BeginTransaction();
            foreach (var frame in frames)
            {
                await InsertFrameAsync(connection, transaction, frame, cancellationToken);
                foreach (var snapshot in frame.Snapshots)
                {
                    await InsertSnapshotAsync(
                        connection,
                        transaction,
                        frame.Diagnostics.FrameIndex,
                        snapshot,
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private async Task InsertFrameAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            PlcAcquisitionFrame frame,
            CancellationToken cancellationToken)
        {
            var diagnostics = frame.Diagnostics;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO plc_sample_frames (
                    session_id,
                    frame_index,
                    planned_timestamp_utc,
                    request_started_timestamp_utc,
                    response_received_timestamp_utc,
                    buffered_timestamp_utc,
                    ui_presented_timestamp_utc,
                    schedule_delay_ms,
                    read_duration_ms,
                    buffer_delay_ms,
                    ui_delay_ms,
                    snapshot_count,
                    state)
                VALUES (
                    $session_id,
                    $frame_index,
                    $planned_timestamp_utc,
                    $request_started_timestamp_utc,
                    $response_received_timestamp_utc,
                    $buffered_timestamp_utc,
                    $ui_presented_timestamp_utc,
                    $schedule_delay_ms,
                    $read_duration_ms,
                    $buffer_delay_ms,
                    $ui_delay_ms,
                    $snapshot_count,
                    $state);
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            command.Parameters.AddWithValue("$frame_index", diagnostics.FrameIndex);
            command.Parameters.AddWithValue("$planned_timestamp_utc", FormatTimestamp(diagnostics.PlannedTimestampUtc));
            command.Parameters.AddWithValue("$request_started_timestamp_utc", FormatTimestamp(diagnostics.RequestStartedTimestampUtc));
            command.Parameters.AddWithValue("$response_received_timestamp_utc", FormatTimestamp(diagnostics.ResponseReceivedTimestampUtc));
            command.Parameters.AddWithValue("$buffered_timestamp_utc", FormatTimestamp(diagnostics.BufferedTimestampUtc));
            command.Parameters.AddWithValue("$ui_presented_timestamp_utc", FormatTimestamp(diagnostics.UiPresentedTimestampUtc));
            command.Parameters.AddWithValue("$schedule_delay_ms", diagnostics.ScheduleDelayMilliseconds);
            command.Parameters.AddWithValue("$read_duration_ms", diagnostics.ReadDurationMilliseconds);
            command.Parameters.AddWithValue("$buffer_delay_ms", diagnostics.BufferDelayMilliseconds);
            command.Parameters.AddWithValue("$ui_delay_ms", diagnostics.UiDelayMilliseconds);
            command.Parameters.AddWithValue("$snapshot_count", diagnostics.SnapshotCount);
            command.Parameters.AddWithValue("$state", (int)diagnostics.State);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task InsertSnapshotAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int frameIndex,
            PlcTagSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO plc_sample_values (
                    session_id,
                    frame_index,
                    tag_id,
                    tag_name,
                    address,
                    value,
                    unit,
                    timestamp_utc,
                    quality,
                    source)
                VALUES (
                    $session_id,
                    $frame_index,
                    $tag_id,
                    $tag_name,
                    $address,
                    $value,
                    $unit,
                    $timestamp_utc,
                    $quality,
                    $source);
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            command.Parameters.AddWithValue("$frame_index", frameIndex);
            command.Parameters.AddWithValue("$tag_id", snapshot.TagId.ToString("D"));
            command.Parameters.AddWithValue("$tag_name", snapshot.Name);
            command.Parameters.AddWithValue("$address", snapshot.Address);
            command.Parameters.AddWithValue("$value", snapshot.Value.HasValue ? snapshot.Value.Value : DBNull.Value);
            command.Parameters.AddWithValue("$unit", snapshot.Unit is null ? DBNull.Value : snapshot.Unit);
            command.Parameters.AddWithValue("$timestamp_utc", FormatTimestamp(snapshot.Timestamp));
            command.Parameters.AddWithValue("$quality", snapshot.Quality);
            command.Parameters.AddWithValue("$source", snapshot.Source);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task MarkStoppedAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE plc_diagnostic_sessions
                SET stopped_at_utc = $stopped_at_utc
                WHERE session_id = $session_id;
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            command.Parameters.AddWithValue("$stopped_at_utc", FormatTimestamp(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private async Task<PlcLiveDiagnosticsSummary> QuerySummaryAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    COUNT(*) AS frame_count,
                    COALESCE(SUM(snapshot_count), 0) AS snapshot_count,
                    COALESCE(AVG(schedule_delay_ms), 0) AS avg_schedule_delay,
                    COALESCE(MAX(schedule_delay_ms), 0) AS max_schedule_delay,
                    COALESCE(AVG(read_duration_ms), 0) AS avg_read_duration,
                    COALESCE(MAX(read_duration_ms), 0) AS max_read_duration,
                    COALESCE(SUM(CASE WHEN state != 0 THEN 1 ELSE 0 END), 0) AS late_count
                FROM plc_sample_frames
                WHERE session_id = $session_id;
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new PlcLiveDiagnosticsSummary(SessionId, DatabasePath, 0, 0, 0, 0, 0, 0, 0);
            }

            return new PlcLiveDiagnosticsSummary(
                SessionId,
                DatabasePath,
                (int)reader.GetInt64(0),
                (int)reader.GetInt64(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                (int)reader.GetInt64(6));
        }
    }
}
