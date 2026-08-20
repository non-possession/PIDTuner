using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcHistoricalTrendCoordinator(IPlcHistoricalTrendStore store)
{
    private static readonly TimeSpan LiveFrameRetentionWindow = TimeSpan.FromHours(1);
    private const int MaximumQueryPointsPerTag = 20_000;
    private const int MinimumBufferedFrameCount = 20_000;
    private const int BufferTransitionPadding = 100;

    private readonly List<IReadOnlyList<PlcTagSnapshot>> _liveFrames = [];

    public void ObserveLiveFrame(PlcAcquisitionFrame frame, int samplingIntervalMilliseconds)
        => ObserveSnapshots(
            frame.Snapshots,
            frame.Diagnostics.PlannedTimestampUtc,
            samplingIntervalMilliseconds);

    public void ObserveSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? timestamp,
        int samplingIntervalMilliseconds)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var frame = timestamp.HasValue
            ? snapshots.Select(snapshot => snapshot with { Timestamp = timestamp.Value }).ToArray()
            : snapshots.ToArray();
        _liveFrames.Add(frame);
        TrimLiveFrames(samplingIntervalMilliseconds);
    }

    public void ClearLiveFrames() => _liveFrames.Clear();

    public async Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> LoadRangeAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var persisted = await store.QueryFramesAsync(
            start,
            end,
            MaximumQueryPointsPerTag,
            cancellationToken);
        var buffered = _liveFrames
            .Where(frame => frame.Count > 0 && frame[0].Timestamp >= start && frame[0].Timestamp <= end);

        return MergeFrames(persisted.Concat(buffered));
    }

    private void TrimLiveFrames(int samplingIntervalMilliseconds)
    {
        var interval = samplingIntervalMilliseconds > 0
            ? samplingIntervalMilliseconds
            : PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
        var maximumFrames = Math.Max(
            MinimumBufferedFrameCount,
            (int)Math.Ceiling(LiveFrameRetentionWindow.TotalMilliseconds / interval) + BufferTransitionPadding);
        if (_liveFrames.Count > maximumFrames)
        {
            _liveFrames.RemoveRange(0, _liveFrames.Count - maximumFrames);
        }
    }

    private static IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> MergeFrames(
        IEnumerable<IReadOnlyList<PlcTagSnapshot>> frames) =>
        frames
            .Where(frame => frame.Count > 0)
            .GroupBy(frame => frame[0].Timestamp)
            .OrderBy(group => group.Key)
            .Select(group => (IReadOnlyList<PlcTagSnapshot>)group
                .SelectMany(frame => frame)
                .GroupBy(snapshot => snapshot.TagId)
                .Select(tagGroup => tagGroup.Last())
                .ToArray())
            .ToArray();
}
