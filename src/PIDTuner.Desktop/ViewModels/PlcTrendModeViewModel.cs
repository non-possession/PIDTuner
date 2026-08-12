using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcTrendModeViewModel : INotifyPropertyChanged
{
    private bool _isHistoricalMode;
    private bool _isLiveScrollingPaused;
    private string _status = "当前趋势：实时";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsHistoricalMode
    {
        get => _isHistoricalMode;
        private set
        {
            if (SetProperty(ref _isHistoricalMode, value))
            {
                OnPropertyChanged(nameof(IsLiveMode));
            }
        }
    }

    public bool IsLiveMode => !IsHistoricalMode;

    public bool IsLiveScrollingPaused
    {
        get => _isLiveScrollingPaused;
        private set
        {
            if (SetProperty(ref _isLiveScrollingPaused, value))
            {
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }
    }

    public string PauseButtonText => IsLiveScrollingPaused ? "恢复滚动" : "暂停滚动";

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public void UseLiveMode()
    {
        IsHistoricalMode = false;
        IsLiveScrollingPaused = false;
        Status = "当前趋势：实时";
    }

    public void UseHistoricalMode()
    {
        IsHistoricalMode = true;
        IsLiveScrollingPaused = true;
        Status = "当前趋势：历史";
    }

    public void MarkHistoricalModeDisplayed()
    {
        IsHistoricalMode = true;
        IsLiveScrollingPaused = false;
        Status = "当前趋势：历史";
    }

    public void ToggleLiveScrollingPause()
    {
        if (IsHistoricalMode)
        {
            return;
        }

        IsLiveScrollingPaused = !IsLiveScrollingPaused;
        Status = IsLiveScrollingPaused
            ? "当前趋势：实时（滚动已暂停）"
            : "当前趋势：实时";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
