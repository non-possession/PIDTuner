using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcOneSecondRecorder(
    Func<PlcProjectConfiguration, CancellationToken, Task<IPlcTagSnapshotReadSession>> openSessionAsync,
    string storageDirectory)
{
    private static readonly TimeSpan RecordingDuration = TimeSpan.FromSeconds(1);

    public async Task<PlcOneSecondRecordingResult> RecordAsync(
        PlcProjectConfiguration configuration,
        Action<IReadOnlyList<PlcTagSnapshot>>? snapshotsCaptured,
        CancellationToken cancellationToken)
    {
        var enabledTags = configuration.Tags
            .Where(tag => tag.IsEnabled && tag.AccessMode != TagAccessMode.WriteOnly)
            .ToArray();
        if (enabledTags.Length == 0)
        {
            return PlcOneSecondRecordingResult.NoReadableTags;
        }

        var intervalMilliseconds = ResolveRecordingIntervalMilliseconds(configuration, enabledTags);
        var frames = new List<IReadOnlyList<PlcTagSnapshot>>();
        var diagnostics = new List<PlcAcquisitionFrameDiagnostics>();
        var stopwatch = Stopwatch.StartNew();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var nextDue = TimeSpan.Zero;
        DateTimeOffset? previousRequestStartedTimestampUtc = null;
        DateTimeOffset? previousResponseReceivedTimestampUtc = null;

        await using var session = await openSessionAsync(configuration, cancellationToken);
        while (nextDue < RecordingDuration)
        {
            var wait = nextDue - stopwatch.Elapsed;
            var catchUpFrame = frames.Count > 0 && wait <= TimeSpan.Zero;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            if (stopwatch.Elapsed >= RecordingDuration)
            {
                break;
            }

            var frameIndex = frames.Count;
            var plannedTimestampUtc = startedAtUtc.Add(nextDue);
            var requestStartedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var snapshots = await session.ReadAsync(cancellationToken);
            var responseReceivedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            frames.Add(snapshots);
            var bufferedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            snapshotsCaptured?.Invoke(snapshots);
            var uiPresentedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var actualIntervalMilliseconds = previousRequestStartedTimestampUtc.HasValue
                ? (requestStartedTimestampUtc - previousRequestStartedTimestampUtc.Value).TotalMilliseconds
                : (double?)null;
            var responseIntervalMilliseconds = previousResponseReceivedTimestampUtc.HasValue
                ? (responseReceivedTimestampUtc - previousResponseReceivedTimestampUtc.Value).TotalMilliseconds
                : (double?)null;

            diagnostics.Add(new PlcAcquisitionFrameDiagnostics(
                frameIndex,
                plannedTimestampUtc,
                requestStartedTimestampUtc,
                responseReceivedTimestampUtc,
                bufferedTimestampUtc,
                uiPresentedTimestampUtc,
                snapshots.Count,
                ClassifyAcquisitionFrame(plannedTimestampUtc, requestStartedTimestampUtc, intervalMilliseconds),
                actualIntervalMilliseconds,
                responseIntervalMilliseconds,
                (requestStartedTimestampUtc - plannedTimestampUtc).TotalMilliseconds,
                catchUpFrame));

            previousRequestStartedTimestampUtc = requestStartedTimestampUtc;
            previousResponseReceivedTimestampUtc = responseReceivedTimestampUtc;
            nextDue += TimeSpan.FromMilliseconds(intervalMilliseconds);
        }

        var diagnosticsSummary = PlcAcquisitionDiagnostics.Summarize(diagnostics);
        var recordingPath = await SaveAsync(configuration, intervalMilliseconds, frames, diagnostics, cancellationToken);
        var snapshotCount = frames.Sum(frame => frame.Count);
        var monitorStatus = $"1s 记录完成：{frames.Count} 组，{enabledTags.Length} 个点位，共 {snapshotCount} 条快照，周期 {intervalMilliseconds} ms。";

        return new PlcOneSecondRecordingResult(
            IsSuccess: true,
            RecordingPath: recordingPath,
            IntervalMilliseconds: intervalMilliseconds,
            EnabledTagCount: enabledTags.Length,
            SnapshotCount: snapshotCount,
            Frames: frames,
            Diagnostics: diagnostics,
            MonitorStatus: monitorStatus,
            DiagnosticsStatus: FormatAcquisitionDiagnosticsSummary(diagnosticsSummary));
    }

    private async Task<string> SaveAsync(
        PlcProjectConfiguration configuration,
        int intervalMilliseconds,
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames,
        IReadOnlyList<PlcAcquisitionFrameDiagnostics> diagnostics,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storageDirectory);
        var fileName = $"plc-recording-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.json";
        var filePath = Path.Combine(storageDirectory, fileName);
        await using var stream = File.Create(filePath);
        var recording = new PlcOneSecondRecording(
            DateTimeOffset.Now,
            configuration.Name,
            configuration.Protocol,
            configuration.IpAddress,
            intervalMilliseconds,
            frames.Count,
            frames.Sum(frame => frame.Count),
            frames,
            diagnostics);

        await JsonSerializer.SerializeAsync(
            stream,
            recording,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            cancellationToken);

        return Path.GetFullPath(filePath);
    }

    private static int ResolveRecordingIntervalMilliseconds(
        PlcProjectConfiguration configuration,
        IReadOnlyList<TagDefinition> enabledTags)
    {
        var minimumTagInterval = enabledTags
            .Select(tag => (int)tag.SamplingInterval.TotalMilliseconds)
            .Where(milliseconds => milliseconds > 0)
            .DefaultIfEmpty(configuration.DefaultSamplingMilliseconds)
            .Min();

        return Math.Max(ResolveMinimumSamplingMilliseconds(configuration), minimumTagInterval);
    }

    private static int ResolveMinimumSamplingMilliseconds(PlcProjectConfiguration configuration)
    {
        return configuration.MinimumSamplingMilliseconds > 0
            ? configuration.MinimumSamplingMilliseconds
            : PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
    }

    private static PlcAcquisitionFrameState ClassifyAcquisitionFrame(
        DateTimeOffset plannedTimestampUtc,
        DateTimeOffset requestStartedTimestampUtc,
        int intervalMilliseconds)
    {
        var lateThresholdMilliseconds = Math.Max(5, intervalMilliseconds * 0.2d);
        return (requestStartedTimestampUtc - plannedTimestampUtc).TotalMilliseconds > lateThresholdMilliseconds
            ? PlcAcquisitionFrameState.Late
            : PlcAcquisitionFrameState.Normal;
    }

    private static string FormatAcquisitionDiagnosticsSummary(PlcAcquisitionDiagnosticsSummary summary)
    {
        if (summary.FrameCount == 0)
        {
            return "诊断：未记录采集帧。";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "诊断：调度延迟 avg {0:0.#} ms / P95 {1:0.#} ms / max {2:0.#} ms；读取耗时 avg {3:0.#} ms / P95 {4:0.#} ms；迟到 {5} 帧。",
            summary.AverageScheduleDelayMilliseconds,
            summary.P95ScheduleDelayMilliseconds,
            summary.MaxScheduleDelayMilliseconds,
            summary.AverageReadDurationMilliseconds,
            summary.P95ReadDurationMilliseconds,
            summary.LateFrameCount);
    }
}

public sealed record PlcOneSecondRecordingResult(
    bool IsSuccess,
    string RecordingPath,
    int IntervalMilliseconds,
    int EnabledTagCount,
    int SnapshotCount,
    IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> Frames,
    IReadOnlyList<PlcAcquisitionFrameDiagnostics> Diagnostics,
    string MonitorStatus,
    string DiagnosticsStatus)
{
    public static PlcOneSecondRecordingResult NoReadableTags { get; } = new(
        IsSuccess: false,
        RecordingPath: string.Empty,
        IntervalMilliseconds: 0,
        EnabledTagCount: 0,
        SnapshotCount: 0,
        Frames: Array.Empty<IReadOnlyList<PlcTagSnapshot>>(),
        Diagnostics: Array.Empty<PlcAcquisitionFrameDiagnostics>(),
        MonitorStatus: string.Empty,
        DiagnosticsStatus: string.Empty);
}

public sealed record PlcOneSecondRecording(
    DateTimeOffset RecordedAt,
    string ConfigurationName,
    string Protocol,
    string IpAddress,
    int IntervalMilliseconds,
    int FrameCount,
    int SnapshotCount,
    IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> Frames,
    IReadOnlyList<PlcAcquisitionFrameDiagnostics>? Diagnostics = null);
