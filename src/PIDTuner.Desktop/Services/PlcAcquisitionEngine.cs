using System.Diagnostics;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcAcquisitionEngine(Func<PlcProjectConfiguration, CancellationToken, Task<IPlcTagSnapshotReadSession>> openSessionAsync)
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

        var effectiveInterval = interval <= TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(100)
            : interval;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _session = await openSessionAsync(configuration, _cancellation.Token);
        _runTask = RunAsync(_session, effectiveInterval, buffer, _cancellation.Token);
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

    private static async Task RunAsync(
        IPlcTagSnapshotReadSession session,
        TimeSpan interval,
        PlcSampleBuffer buffer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var nextDue = TimeSpan.Zero;
        var frameIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var wait = nextDue - stopwatch.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            var plannedTimestampUtc = startedAtUtc.Add(nextDue);
            var requestStartedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var snapshots = await session.ReadAsync(cancellationToken);
            var responseReceivedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var bufferedTimestampUtc = startedAtUtc.Add(stopwatch.Elapsed);
            var uiPresentedTimestampUtc = bufferedTimestampUtc;
            buffer.Add(new PlcAcquisitionFrame(
                snapshots,
                new PlcAcquisitionFrameDiagnostics(
                    frameIndex,
                    plannedTimestampUtc,
                    requestStartedTimestampUtc,
                    responseReceivedTimestampUtc,
                    bufferedTimestampUtc,
                    uiPresentedTimestampUtc,
                    snapshots.Count,
                    ClassifyFrame(plannedTimestampUtc, requestStartedTimestampUtc, interval))));

            frameIndex++;
            nextDue += interval;
        }
    }

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
