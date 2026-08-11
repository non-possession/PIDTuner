using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcLiveMonitorViewModel : INotifyPropertyChanged
{
    private bool _isMonitoring;
    private bool _isLiveTrendPaused;
    private int _currentAcquisitionIntervalMilliseconds;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlcLiveMonitorViewModel(ObservableCollection<PlcTagMonitorViewModel> tags)
    {
        Tags = tags;
    }

    public ObservableCollection<PlcTagMonitorViewModel> Tags { get; }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set => SetProperty(ref _isMonitoring, value);
    }

    public bool IsLiveTrendPaused
    {
        get => _isLiveTrendPaused;
        set => SetProperty(ref _isLiveTrendPaused, value);
    }

    public int CurrentAcquisitionIntervalMilliseconds
    {
        get => _currentAcquisitionIntervalMilliseconds;
        set => SetProperty(ref _currentAcquisitionIntervalMilliseconds, value);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
