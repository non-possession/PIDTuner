using System.Diagnostics;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcAcquisitionEngine(
    Func<PlcProjectConfiguration, CancellationToken, Task<IPlcTagSnapshotReadSession>> openSessionAsync,
    Action<PlcAcquisitionFrame>? frameAcquired = null)
    : IAsyncDisposable
{
    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private IPlcTagSnapshotReadSession? _session;

    public bool IsRunning => _runTask is not null && !_runTask.IsCompleted;

    public async Task StartAsync(
        PlcProjectConfiguration configuration,
        TimeSpan interval,
        PlcSampleBuffer buffer,
        CancellationToken cancellationToken)
    {
        await StopAsync();

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "PLC acquisition interval must be greater than zero.");
        }

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _session = await openSessionAsync(configuration, _cancellation.Token);
        _runTask = RunAsync(_session, interval, buffer, _cancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
        }

        if (_runTask is not null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_session is not null)
        {
            await _session.DisposeAsync();
        }

        _session = null;
        _runTask = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task RunAsync(
        IPlcTagSnapshotReadSession session,
        TimeSpan interval,
        PlcSampleBuffer buffer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var nextDue = TimeSpan.Zero;
        var frameIndex = 0;
        DateTimeOffset? previousRequestStartedTimestampUtc = null;
        DateTimeOffset? previousResponseReceivedTimestampUtc = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = nextDue - stopwatch.Elapsed;
            var catchUpFrame = frameIndex > 0 && wait <= TimeSpan.Zero;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            var plannedTimestampUtc = startedAtUtc.Add(nextDue);
            var requestStartedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var snapshots = await session.ReadAsync(cancellationToken);
            var readOperations = session.LastReadDiagnostics.ToArray();
            var responseReceivedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var bufferedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var uiPresentedTimestampUtc = bufferedTimestampUtc;
            var scheduleAdvance = CalculateScheduleAdvance(nextDue, stopwatch.Elapsed, interval);
            var actualIntervalMilliseconds = previousRequestStartedTimestampUtc.HasValue
                ? (requestStartedTimestampUtc - previousRequestStartedTimestampUtc.Value).TotalMilliseconds
                : (double?)null;
            var responseIntervalMilliseconds = previousResponseReceivedTimestampUtc.HasValue
                ? (responseReceivedTimestampUtc - previousResponseReceivedTimestampUtc.Value).TotalMilliseconds
                : (double?)null;
            var requestElapsed = requestStartedTimestampUtc - startedAtUtc;
            var frame = new PlcAcquisitionFrame(
                snapshots,
                new PlcAcquisitionFrameDiagnostics(
                    frameIndex,
                    plannedTimestampUtc,
                    requestStartedTimestampUtc,
                    responseReceivedTimestampUtc,
                    bufferedTimestampUtc,
                    uiPresentedTimestampUtc,
                    snapshots.Count,
                    ClassifyFrame(plannedTimestampUtc, requestStartedTimestampUtc, interval),
                    actualIntervalMilliseconds,
                    responseIntervalMilliseconds,
                    (requestStartedTimestampUtc - plannedTimestampUtc).TotalMilliseconds,
                    catchUpFrame,
                    nextDue.TotalMilliseconds,
                    requestElapsed.TotalMilliseconds,
                    CalculateScheduleSlotIndex(nextDue, interval),
                    scheduleAdvance.SkippedScheduleSlots,
                    PhaseMilliseconds(nextDue, 1_000),
                    PhaseMilliseconds(nextDue, 5_000),
                    PhaseMilliseconds(nextDue, 10_000),
                    PhaseMilliseconds(nextDue, 11_000),
                    PhaseMilliseconds(requestElapsed, 1_000),
                    PhaseMilliseconds(requestElapsed, 5_000),
                    PhaseMilliseconds(requestElapsed, 10_000),
                    PhaseMilliseconds(requestElapsed, 11_000)),
                readOperations);
            frameAcquired?.Invoke(frame);
            buffer.Add(frame);

            previousRequestStartedTimestampUtc = requestStartedTimestampUtc;
            previousResponseReceivedTimestampUtc = responseReceivedTimestampUtc;
            frameIndex++;
            nextDue = scheduleAdvance.NextDue;
        }
    }

    public static TimeSpan AdvanceNextDue(TimeSpan currentDue, TimeSpan elapsed, TimeSpan interval)
    {
        return CalculateScheduleAdvance(currentDue, elapsed, interval).NextDue;
    }

    public static PlcScheduleAdvance CalculateScheduleAdvance(TimeSpan currentDue, TimeSpan elapsed, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "PLC acquisition interval must be greater than zero.");
        }

        var nextDue = currentDue + interval;
        if (elapsed < nextDue)
        {
            return new PlcScheduleAdvance(nextDue, 0);
        }

        var missedIntervals = ((elapsed - nextDue).Ticks / interval.Ticks) + 1;
        return new PlcScheduleAdvance(
            nextDue + TimeSpan.FromTicks(missedIntervals * interval.Ticks),
            missedIntervals > int.MaxValue ? int.MaxValue : (int)missedIntervals);
    }

    private static long CalculateScheduleSlotIndex(TimeSpan plannedElapsed, TimeSpan interval) =>
        interval.Ticks <= 0 ? 0 : plannedElapsed.Ticks / interval.Ticks;

    private static double PhaseMilliseconds(TimeSpan elapsed, int periodMilliseconds)
    {
        var periodTicks = TimeSpan.FromMilliseconds(periodMilliseconds).Ticks;
        var phaseTicks = elapsed.Ticks % periodTicks;
        if (phaseTicks < 0)
        {
            phaseTicks += periodTicks;
        }

        return TimeSpan.FromTicks(phaseTicks).TotalMilliseconds;
    }

    public readonly record struct PlcScheduleAdvance(TimeSpan NextDue, int SkippedScheduleSlots);

    private static PlcAcquisitionFrameState ClassifyFrame(
        DateTimeOffset plannedTimestampUtc,
        DateTimeOffset requestStartedTimestampUtc,
        TimeSpan interval)
    {
        var lateThresholdMilliseconds = Math.Max(5, interval.TotalMilliseconds * 0.2d);
        return (requestStartedTimestampUtc - plannedTimestampUtc).TotalMilliseconds > lateThresholdMilliseconds
            ? PlcAcquisitionFrameState.Late
            : PlcAcquisitionFrameState.Normal;
    }
}
