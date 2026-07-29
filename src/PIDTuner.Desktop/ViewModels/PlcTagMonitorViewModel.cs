using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcTagMonitorViewModel : INotifyPropertyChanged
{
    public PlcTagMonitorViewModel(PlcTagSnapshot snapshot)
    {
        TagId = snapshot.TagId;
        Name = snapshot.Name;
        Address = snapshot.Address;
        Unit = snapshot.Unit ?? string.Empty;
        Quality = snapshot.Quality;
        Source = snapshot.Source;
        Update(snapshot);
    }

    public Guid TagId { get; }

    public string Name { get; }

    public string Address { get; }

    public string Unit { get; }

    private string _valueText = "-";
    private string _timestampText = "-";
    private string _quality = string.Empty;
    private string _source = string.Empty;
    private PointCollection _trendPoints = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ValueText
    {
        get => _valueText;
        private set => SetProperty(ref _valueText, value);
    }

    public string TimestampText
    {
        get => _timestampText;
        private set => SetProperty(ref _timestampText, value);
    }

    public string Quality
    {
        get => _quality;
        private set => SetProperty(ref _quality, value);
    }

    public string Source
    {
        get => _source;
        private set => SetProperty(ref _source, value);
    }

    public PointCollection TrendPoints
    {
        get => _trendPoints;
        private set => SetProperty(ref _trendPoints, value);
    }

    private readonly Queue<double> _values = new();

    public void Update(PlcTagSnapshot snapshot)
    {
        ValueText = snapshot.Value?.ToString("0.###") ?? "-";
        TimestampText = snapshot.Timestamp.ToString("HH:mm:ss");
        Quality = snapshot.Quality;
        Source = snapshot.Source;

        if (snapshot.Value is double value)
        {
            _values.Enqueue(value);
            while (_values.Count > 30)
            {
                _values.Dequeue();
            }
        }

        TrendPoints = BuildTrendPoints(_values.ToArray());
    }

    private static PointCollection BuildTrendPoints(IReadOnlyList<double> values)
    {
        var points = new PointCollection();
        if (values.Count == 0)
        {
            return points;
        }

        var min = values.Min();
        var max = values.Max();
        var span = Math.Abs(max - min) < 0.000001 ? 1 : max - min;
        var denominator = Math.Max(1, values.Count - 1);

        for (var index = 0; index < values.Count; index++)
        {
            var x = index * 260.0 / denominator;
            var y = 70 - ((values[index] - min) / span * 60 + 5);
            points.Add(new System.Windows.Point(x, y));
        }

        return points;
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
