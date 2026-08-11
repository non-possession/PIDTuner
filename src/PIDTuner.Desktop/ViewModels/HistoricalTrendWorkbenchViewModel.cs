using System.ComponentModel;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Trends;

namespace PIDTuner.Desktop.ViewModels;

public sealed class HistoricalTrendWorkbenchViewModel : INotifyPropertyChanged
{
    private readonly HistoricalTrendWorkbenchCoordinator _coordinator = new();
    private HistoricalTrendWorkbenchState _state = new(
        new HistoricalTrendDataset(Array.Empty<HistoricalTrendSeries>()),
        null,
        null,
        new HashSet<Guid>());

    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoricalTrendWorkbenchState State
    {
        get => _state;
        private set
        {
            if (Equals(_state, value))
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleSeries));
            OnPropertyChanged(nameof(VisibleTimeRange));
            OnPropertyChanged(nameof(VisibleYRange));
            OnPropertyChanged(nameof(HasDataset));
        }
    }

    public IReadOnlyList<HistoricalTrendSeries> VisibleSeries => _coordinator.GetVisibleSeries(State);

    public TrendTimeRange? VisibleTimeRange => State.VisibleTimeRange;

    public TrendNumericRange? VisibleYRange => State.VisibleYRange;

    public bool HasDataset => !State.Dataset.IsEmpty;

    public void LoadDataset(HistoricalTrendDataset dataset)
    {
        State = _coordinator.LoadDataset(dataset);
    }

    public void SetVisibleTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        State = _coordinator.SetVisibleTimeRange(State, start, end);
    }

    public void ResetVisibleTimeRange()
    {
        State = _coordinator.ResetVisibleTimeRange(State);
    }

    public void SetVisibleYRange(double minimum, double maximum)
    {
        State = _coordinator.SetVisibleYRange(State, minimum, maximum);
    }

    public void ResetVisibleYRange()
    {
        State = _coordinator.ResetVisibleYRange(State);
    }

    public void SetSeriesVisibility(Guid seriesId, bool isVisible)
    {
        State = _coordinator.SetSeriesVisibility(State, seriesId, isVisible);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
