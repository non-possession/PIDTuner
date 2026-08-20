using System.Globalization;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Persistence;

public sealed class SqlitePlcLiveDiagnosticsStore(string databasePath) : IPlcLiveDiagnosticsStore
{
    private const int MaxQueuedFrames = 50_000;

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
                actual_interval_ms REAL NULL,
                response_interval_ms REAL NULL,
                phase_error_ms REAL NULL,
                catch_up_frame INTEGER NOT NULL DEFAULT 0,
                planned_elapsed_ms REAL NULL,
                request_elapsed_ms REAL NULL,
                schedule_slot_index INTEGER NULL,
                skipped_schedule_slots INTEGER NOT NULL DEFAULT 0,
                planned_phase_1000_ms REAL NULL,
                planned_phase_5000_ms REAL NULL,
                planned_phase_10000_ms REAL NULL,
                planned_phase_11000_ms REAL NULL,
                request_phase_1000_ms REAL NULL,
                request_phase_5000_ms REAL NULL,
                request_phase_10000_ms REAL NULL,
                request_phase_11000_ms REAL NULL,
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

            CREATE TABLE IF NOT EXISTS plc_read_operations (
                session_id TEXT NOT NULL,
                frame_index INTEGER NOT NULL,
                operation_index INTEGER NOT NULL,
                operation_kind TEXT NOT NULL,
                target TEXT NOT NULL,
                address_count INTEGER NOT NULL,
                request_started_timestamp_utc TEXT NOT NULL,
                response_received_timestamp_utc TEXT NOT NULL,
                duration_ms REAL NOT NULL,
                send_duration_ms REAL NOT NULL DEFAULT 0,
                receive_header_duration_ms REAL NOT NULL DEFAULT 0,
                receive_payload_duration_ms REAL NOT NULL DEFAULT 0,
                success_count INTEGER NOT NULL,
                failure_count INTEGER NOT NULL,
                error TEXT NULL,
                error_category TEXT NOT NULL DEFAULT 'None',
                error_code TEXT NULL,
                error_context TEXT NULL,
                is_transient INTEGER NOT NULL DEFAULT 0,
                request_pdu_reference INTEGER NULL,
                response_pdu_reference INTEGER NULL,
                PRIMARY KEY (session_id, frame_index, operation_index)
            );

            CREATE INDEX IF NOT EXISTS idx_plc_sample_values_session_time
                ON plc_sample_values(session_id, timestamp_utc);

            CREATE INDEX IF NOT EXISTS idx_plc_sample_frames_session_time
                ON plc_sample_frames(session_id, planned_timestamp_utc);

            CREATE INDEX IF NOT EXISTS idx_plc_read_operations_session_frame
                ON plc_read_operations(session_id, frame_index);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureSampleFrameTimingColumnsAsync(connection, cancellationToken);
        await EnsureReadOperationTimingColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureSampleFrameTimingColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "PRAGMA table_info(plc_sample_frames);";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var (column, definition) in new[]
        {
            ("actual_interval_ms", "REAL NULL"),
            ("response_interval_ms", "REAL NULL"),
            ("phase_error_ms", "REAL NULL"),
            ("catch_up_frame", "INTEGER NOT NULL DEFAULT 0"),
            ("planned_elapsed_ms", "REAL NULL"),
            ("request_elapsed_ms", "REAL NULL"),
            ("schedule_slot_index", "INTEGER NULL"),
            ("skipped_schedule_slots", "INTEGER NOT NULL DEFAULT 0"),
            ("planned_phase_1000_ms", "REAL NULL"),
            ("planned_phase_5000_ms", "REAL NULL"),
            ("planned_phase_10000_ms", "REAL NULL"),
            ("planned_phase_11000_ms", "REAL NULL"),
            ("request_phase_1000_ms", "REAL NULL"),
            ("request_phase_5000_ms", "REAL NULL"),
            ("request_phase_10000_ms", "REAL NULL"),
            ("request_phase_11000_ms", "REAL NULL")
        })
        {
            if (existingColumns.Contains(column))
            {
                continue;
            }

            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE plc_sample_frames ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureReadOperationTimingColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var query = connection.CreateCommand())
        {
            query.CommandText = "PRAGMA table_info(plc_read_operations);";
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        foreach (var (column, definition) in new[]
        {
            ("send_duration_ms", "REAL NOT NULL DEFAULT 0"),
            ("receive_header_duration_ms", "REAL NOT NULL DEFAULT 0"),
            ("receive_payload_duration_ms", "REAL NOT NULL DEFAULT 0"),
            ("error_category", "TEXT NOT NULL DEFAULT 'None'"),
            ("error_code", "TEXT NULL"),
            ("error_context", "TEXT NULL"),
            ("is_transient", "INTEGER NOT NULL DEFAULT 0"),
            ("request_pdu_reference", "INTEGER NULL"),
            ("response_pdu_reference", "INTEGER NULL")
        })
        {
            if (existingColumns.Contains(column))
            {
                continue;
            }

            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE plc_read_operations ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
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
        private int _droppedFrameCount;
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

            if (!_frames.Writer.TryWrite(frame))
            {
                Interlocked.Increment(ref _droppedFrameCount);
            }
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

                foreach (var operation in frame.ReadOperations)
                {
                    await InsertReadOperationAsync(
                        connection,
                        transaction,
                        frame.Diagnostics.FrameIndex,
                        operation,
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
                    actual_interval_ms,
                    response_interval_ms,
                    phase_error_ms,
                    catch_up_frame,
                    planned_elapsed_ms,
                    request_elapsed_ms,
                    schedule_slot_index,
                    skipped_schedule_slots,
                    planned_phase_1000_ms,
                    planned_phase_5000_ms,
                    planned_phase_10000_ms,
                    planned_phase_11000_ms,
                    request_phase_1000_ms,
                    request_phase_5000_ms,
                    request_phase_10000_ms,
                    request_phase_11000_ms,
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
                    $actual_interval_ms,
                    $response_interval_ms,
                    $phase_error_ms,
                    $catch_up_frame,
                    $planned_elapsed_ms,
                    $request_elapsed_ms,
                    $schedule_slot_index,
                    $skipped_schedule_slots,
                    $planned_phase_1000_ms,
                    $planned_phase_5000_ms,
                    $planned_phase_10000_ms,
                    $planned_phase_11000_ms,
                    $request_phase_1000_ms,
                    $request_phase_5000_ms,
                    $request_phase_10000_ms,
                    $request_phase_11000_ms,
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
            command.Parameters.AddWithValue(
                "$actual_interval_ms",
                diagnostics.ActualIntervalMilliseconds.HasValue ? diagnostics.ActualIntervalMilliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$response_interval_ms",
                diagnostics.ResponseIntervalMilliseconds.HasValue ? diagnostics.ResponseIntervalMilliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$phase_error_ms",
                diagnostics.PhaseErrorMilliseconds.HasValue ? diagnostics.PhaseErrorMilliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue("$catch_up_frame", diagnostics.CatchUpFrame ? 1 : 0);
            command.Parameters.AddWithValue(
                "$planned_elapsed_ms",
                diagnostics.PlannedElapsedMilliseconds.HasValue ? diagnostics.PlannedElapsedMilliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$request_elapsed_ms",
                diagnostics.RequestElapsedMilliseconds.HasValue ? diagnostics.RequestElapsedMilliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$schedule_slot_index",
                diagnostics.ScheduleSlotIndex.HasValue ? diagnostics.ScheduleSlotIndex.Value : DBNull.Value);
            command.Parameters.AddWithValue("$skipped_schedule_slots", diagnostics.SkippedScheduleSlots);
            command.Parameters.AddWithValue(
                "$planned_phase_1000_ms",
                diagnostics.PlannedPhase1000Milliseconds.HasValue ? diagnostics.PlannedPhase1000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$planned_phase_5000_ms",
                diagnostics.PlannedPhase5000Milliseconds.HasValue ? diagnostics.PlannedPhase5000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$planned_phase_10000_ms",
                diagnostics.PlannedPhase10000Milliseconds.HasValue ? diagnostics.PlannedPhase10000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$planned_phase_11000_ms",
                diagnostics.PlannedPhase11000Milliseconds.HasValue ? diagnostics.PlannedPhase11000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$request_phase_1000_ms",
                diagnostics.RequestPhase1000Milliseconds.HasValue ? diagnostics.RequestPhase1000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$request_phase_5000_ms",
                diagnostics.RequestPhase5000Milliseconds.HasValue ? diagnostics.RequestPhase5000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$request_phase_10000_ms",
                diagnostics.RequestPhase10000Milliseconds.HasValue ? diagnostics.RequestPhase10000Milliseconds.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$request_phase_11000_ms",
                diagnostics.RequestPhase11000Milliseconds.HasValue ? diagnostics.RequestPhase11000Milliseconds.Value : DBNull.Value);
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

        private async Task InsertReadOperationAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            int frameIndex,
            PlcReadOperationDiagnostics operation,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO plc_read_operations (
                    session_id,
                    frame_index,
                    operation_index,
                    operation_kind,
                    target,
                    address_count,
                    request_started_timestamp_utc,
                    response_received_timestamp_utc,
                    duration_ms,
                    send_duration_ms,
                    receive_header_duration_ms,
                    receive_payload_duration_ms,
                    success_count,
                    failure_count,
                    error,
                    error_category,
                    error_code,
                    error_context,
                    is_transient,
                    request_pdu_reference,
                    response_pdu_reference)
                VALUES (
                    $session_id,
                    $frame_index,
                    $operation_index,
                    $operation_kind,
                    $target,
                    $address_count,
                    $request_started_timestamp_utc,
                    $response_received_timestamp_utc,
                    $duration_ms,
                    $send_duration_ms,
                    $receive_header_duration_ms,
                    $receive_payload_duration_ms,
                    $success_count,
                    $failure_count,
                    $error,
                    $error_category,
                    $error_code,
                    $error_context,
                    $is_transient,
                    $request_pdu_reference,
                    $response_pdu_reference);
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            command.Parameters.AddWithValue("$frame_index", frameIndex);
            command.Parameters.AddWithValue("$operation_index", operation.OperationIndex);
            command.Parameters.AddWithValue("$operation_kind", operation.OperationKind);
            command.Parameters.AddWithValue("$target", operation.Target);
            command.Parameters.AddWithValue("$address_count", operation.AddressCount);
            command.Parameters.AddWithValue("$request_started_timestamp_utc", FormatTimestamp(operation.RequestStartedTimestampUtc));
            command.Parameters.AddWithValue("$response_received_timestamp_utc", FormatTimestamp(operation.ResponseReceivedTimestampUtc));
            command.Parameters.AddWithValue("$duration_ms", operation.DurationMilliseconds);
            command.Parameters.AddWithValue("$send_duration_ms", operation.SendDurationMilliseconds);
            command.Parameters.AddWithValue("$receive_header_duration_ms", operation.ReceiveHeaderDurationMilliseconds);
            command.Parameters.AddWithValue("$receive_payload_duration_ms", operation.ReceivePayloadDurationMilliseconds);
            command.Parameters.AddWithValue("$success_count", operation.SuccessCount);
            command.Parameters.AddWithValue("$failure_count", operation.FailureCount);
            command.Parameters.AddWithValue("$error", operation.Error is null ? DBNull.Value : operation.Error);
            command.Parameters.AddWithValue("$error_category", operation.ErrorCategory.ToString());
            command.Parameters.AddWithValue("$error_code", operation.ErrorCode is null ? DBNull.Value : operation.ErrorCode);
            command.Parameters.AddWithValue("$error_context", operation.ErrorContext is null ? DBNull.Value : operation.ErrorContext);
            command.Parameters.AddWithValue("$is_transient", operation.IsTransient ? 1 : 0);
            command.Parameters.AddWithValue(
                "$request_pdu_reference",
                operation.RequestPduReference.HasValue ? operation.RequestPduReference.Value : DBNull.Value);
            command.Parameters.AddWithValue(
                "$response_pdu_reference",
                operation.ResponsePduReference.HasValue ? operation.ResponsePduReference.Value : DBNull.Value);
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
                return new PlcLiveDiagnosticsSummary(
                    SessionId,
                    DatabasePath,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    Volatile.Read(ref _droppedFrameCount));
            }

            var frameSummary = new PlcLiveDiagnosticsSummary(
                SessionId,
                DatabasePath,
                (int)reader.GetInt64(0),
                (int)reader.GetInt64(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                (int)reader.GetInt64(6),
                0,
                0,
                0,
                0,
                Volatile.Read(ref _droppedFrameCount));

            return await QueryReadOperationSummaryAsync(connection, frameSummary, cancellationToken);
        }

        private async Task<PlcLiveDiagnosticsSummary> QueryReadOperationSummaryAsync(
            SqliteConnection connection,
            PlcLiveDiagnosticsSummary frameSummary,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    COUNT(*) AS operation_count,
                    COALESCE(AVG(duration_ms), 0) AS avg_operation_duration,
                    COALESCE(MAX(duration_ms), 0) AS max_operation_duration,
                    COALESCE(SUM(
                        CASE
                            WHEN duration_ms > (
                                SELECT default_sampling_ms * 0.8
                                FROM plc_diagnostic_sessions
                                WHERE session_id = $session_id
                            )
                            THEN 1
                            ELSE 0
                        END), 0) AS slow_operation_count
                FROM plc_read_operations
                WHERE session_id = $session_id;
                """;
            command.Parameters.AddWithValue("$session_id", SessionId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return frameSummary;
            }

            return frameSummary with
            {
                ReadOperationCount = (int)reader.GetInt64(0),
                AverageReadOperationDurationMilliseconds = reader.GetDouble(1),
                MaxReadOperationDurationMilliseconds = reader.GetDouble(2),
                SlowReadOperationCount = (int)reader.GetInt64(3)
            };
        }
    }
}
