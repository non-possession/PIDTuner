using System.Collections.ObjectModel;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcMonitorSnapshotPresenter(ObservableCollection<PlcTagMonitorViewModel> tags)
{
    public event Action<IReadOnlyList<PlcTagSnapshot>, DateTimeOffset?>? SnapshotsApplied;

    public PlcTagMonitorViewModel? SelectedTag { get; set; }

    public void Apply(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? trendTimestamp = null,
        bool applyTrend = true)
    {
        foreach (var snapshot in snapshots)
        {
            var existing = tags.FirstOrDefault(item => item.TagId == snapshot.TagId);
            if (existing is null)
            {
                tags.Add(new PlcTagMonitorViewModel(snapshot));
                continue;
            }

            existing.Update(snapshot);
        }

        var activeIds = snapshots.Select(snapshot => snapshot.TagId).ToHashSet();
        for (var index = tags.Count - 1; index >= 0; index--)
        {
            if (!activeIds.Contains(tags[index].TagId))
            {
                tags.RemoveAt(index);
            }
        }

        SelectedTag ??= tags.FirstOrDefault();
        if (applyTrend)
        {
            SnapshotsApplied?.Invoke(snapshots, trendTimestamp);
        }
    }
}
