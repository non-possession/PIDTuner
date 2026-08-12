using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PIDTuner.Desktop.ViewModels;

public sealed class NotificationViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _message = string.Empty;
    private string _kind = "Info";
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string Kind
    {
        get => _kind;
        private set => SetProperty(ref _kind, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public void Show(string title, string message, string kind)
    {
        Title = title;
        Message = message;
        Kind = kind;
        IsVisible = true;
    }

    public void Dismiss()
    {
        IsVisible = false;
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
