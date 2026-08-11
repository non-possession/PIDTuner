using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcDebugViewModel : INotifyPropertyChanged
{
    private string _diagnosticsStatus = "实时诊断：尚未启动。";
    private string _replayStatus = "尚未加载 PLC 记录。";
    private bool _isDiagnosticsRunning;
    private bool _isReplayRunning;
    private double _replaySpeedMultiplier = 1d;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlcDebugViewModel(ObservableCollection<PlcTagMonitorViewModel> detailedTags)
    {
        DetailedTags = detailedTags;
    }

    public ObservableCollection<PlcTagMonitorViewModel> DetailedTags { get; }

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        set => SetProperty(ref _diagnosticsStatus, value);
    }

    public string ReplayStatus
    {
        get => _replayStatus;
        set => SetProperty(ref _replayStatus, value);
    }

    public bool IsDiagnosticsRunning
    {
        get => _isDiagnosticsRunning;
        set => SetProperty(ref _isDiagnosticsRunning, value);
    }

    public bool IsReplayRunning
    {
        get => _isReplayRunning;
        set => SetProperty(ref _isReplayRunning, value);
    }

    public double ReplaySpeedMultiplier
    {
        get => _replaySpeedMultiplier;
        set => SetProperty(ref _replaySpeedMultiplier, value);
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
