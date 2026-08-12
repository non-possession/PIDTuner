using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Plc;
using PIDTuner.Domain.Trends;

namespace PIDTuner.Desktop.ViewModels;

public sealed class HistoricalTrendWorkbenchViewModel : INotifyPropertyChanged
{
    private const double AxisSliderMinimum = 0d;
    private const double AxisSliderMaximum = 1000d;

    private readonly HistoricalTrendWorkbenchCoordinator _coordinator = new();
    private readonly Dictionary<Guid, SeriesAxisRangeState> _seriesAxisRanges = [];
    private HistoricalTrendWorkbenchState _state = new(
        new HistoricalTrendDataset(Array.Empty<HistoricalTrendSeries>()),
        null,
        null,
        new HashSet<Guid>());
    private string _rangeStartText = string.Empty;
    private string _rangeEndText = string.Empty;
    private string _yMinimumText = string.Empty;
    private string _yMaximumText = string.Empty;
    private string _rightYMinimumText = string.Empty;
    private string _rightYMaximumText = string.Empty;
    private string _viewportStartLabel = string.Empty;
    private string _viewportEndLabel = string.Empty;
    private double _viewportMinimum;
    private double _viewportMaximum = 1d;
    private double _viewportStart;
    private double _viewportEnd = 1d;
    private double _ySliderMinimum;
    private double _ySliderMaximum = 1d;
    private double _yLower;
    private double _yUpper = 1d;
    private double _rightYLower;
    private double _rightYUpper = 1d;
    private DateTimeOffset? _totalStart;
    private DateTimeOffset? _totalEnd;
    private double _totalYMinimum;
    private double _totalYMaximum = 1d;
    private double _totalRightYMinimum;
    private double _totalRightYMaximum = 1d;
    private bool _isViewportEnabled;
    private bool _isYSliderEnabled;
    private bool _isDualAxisLayout = true;
    private Guid? _selectedLeftAxisSeriesId;
    private Guid? _selectedRightAxisSeriesId;
    private bool _isUpdatingSliderState;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<DateTimeOffset, DateTimeOffset>? ViewportRequested;

    public event Action<double, double>? YRangeRequested;

    public event Action<double, double>? RightYRangeRequested;

    public event Action<string, string?>? StatusRequested;

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

    public string RangeStartText
    {
        get => _rangeStartText;
        set => SetProperty(ref _rangeStartText, value);
    }

    public string RangeEndText
    {
        get => _rangeEndText;
        set => SetProperty(ref _rangeEndText, value);
    }

    public string YMinimumText
    {
        get => _yMinimumText;
        set => SetProperty(ref _yMinimumText, value);
    }

    public string YMaximumText
    {
        get => _yMaximumText;
        set => SetProperty(ref _yMaximumText, value);
    }

    public string RightYMinimumText
    {
        get => _rightYMinimumText;
        private set => SetProperty(ref _rightYMinimumText, value);
    }

    public string RightYMaximumText
    {
        get => _rightYMaximumText;
        private set => SetProperty(ref _rightYMaximumText, value);
    }

    public string ViewportStartLabel
    {
        get => _viewportStartLabel;
        private set => SetProperty(ref _viewportStartLabel, value);
    }

    public string ViewportEndLabel
    {
        get => _viewportEndLabel;
        private set => SetProperty(ref _viewportEndLabel, value);
    }

    public double ViewportMinimum
    {
        get => _viewportMinimum;
        private set => SetProperty(ref _viewportMinimum, value);
    }

    public double ViewportMaximum
    {
        get => _viewportMaximum;
        private set => SetProperty(ref _viewportMaximum, value);
    }

    public double ViewportStart
    {
        get => _viewportStart;
        set
        {
            var clamped = ClampAxisSliderValue(value, ViewportMinimum, ViewportMaximum);
            if (SetProperty(ref _viewportStart, clamped))
            {
                ApplyViewportSliderChange();
            }
        }
    }

    public double ViewportEnd
    {
        get => _viewportEnd;
        set
        {
            var clamped = ClampAxisSliderValue(value, ViewportMinimum, ViewportMaximum);
            if (SetProperty(ref _viewportEnd, clamped))
            {
                ApplyViewportSliderChange();
            }
        }
    }

    public double YSliderMinimum
    {
        get => _ySliderMinimum;
        private set => SetProperty(ref _ySliderMinimum, value);
    }

    public double YSliderMaximum
    {
        get => _ySliderMaximum;
        private set => SetProperty(ref _ySliderMaximum, value);
    }

    public double YLower
    {
        get => _yLower;
        set
        {
            var clamped = ClampAxisSliderValue(value, YSliderMinimum, YSliderMaximum);
            if (SetProperty(ref _yLower, clamped))
            {
                ApplyYSliderChange();
            }
        }
    }

    public double YUpper
    {
        get => _yUpper;
        set
        {
            var clamped = ClampAxisSliderValue(value, YSliderMinimum, YSliderMaximum);
            if (SetProperty(ref _yUpper, clamped))
            {
                ApplyYSliderChange();
            }
        }
    }

    public double RightYLower
    {
        get => _rightYLower;
        set
        {
            var clamped = ClampAxisSliderValue(value, YSliderMinimum, YSliderMaximum);
            if (SetProperty(ref _rightYLower, clamped))
            {
                ApplyRightYSliderChange();
            }
        }
    }

    public double RightYUpper
    {
        get => _rightYUpper;
        set
        {
            var clamped = ClampAxisSliderValue(value, YSliderMinimum, YSliderMaximum);
            if (SetProperty(ref _rightYUpper, clamped))
            {
                ApplyRightYSliderChange();
            }
        }
    }

    public Guid? SelectedLeftAxisSeriesId
    {
        get => _selectedLeftAxisSeriesId;
        set
        {
            if (SetProperty(ref _selectedLeftAxisSeriesId, value))
            {
                ResetLeftAxisRangeFromSelectedSeries();
            }
        }
    }

    public Guid? SelectedRightAxisSeriesId
    {
        get => _selectedRightAxisSeriesId;
        set
        {
            if (SetProperty(ref _selectedRightAxisSeriesId, value))
            {
                ResetRightAxisRangeFromSelectedSeries();
            }
        }
    }

    public bool IsViewportEnabled
    {
        get => _isViewportEnabled;
        private set => SetProperty(ref _isViewportEnabled, value);
    }

    public bool IsYSliderEnabled
    {
        get => _isYSliderEnabled;
        private set => SetProperty(ref _isYSliderEnabled, value);
    }

    public bool IsSingleAxisLayout => !IsDualAxisLayout;

    public bool IsDualAxisLayout
    {
        get => _isDualAxisLayout;
        private set
        {
            if (SetProperty(ref _isDualAxisLayout, value))
            {
                OnPropertyChanged(nameof(IsSingleAxisLayout));
            }
        }
    }

    public void LoadDataset(HistoricalTrendDataset dataset)
    {
        State = _coordinator.LoadDataset(dataset);
    }

    public void Clear()
    {
        State = _coordinator.LoadDataset(new HistoricalTrendDataset(Array.Empty<HistoricalTrendSeries>()));
        _seriesAxisRanges.Clear();
        IsViewportEnabled = false;
        IsYSliderEnabled = false;
        RangeStartText = string.Empty;
        RangeEndText = string.Empty;
        YMinimumText = string.Empty;
        YMaximumText = string.Empty;
        RightYMinimumText = string.Empty;
        RightYMaximumText = string.Empty;
        ViewportStartLabel = string.Empty;
        ViewportEndLabel = string.Empty;
        SelectedLeftAxisSeriesId = null;
        SelectedRightAxisSeriesId = null;
    }

    public void UseSingleAxisLayout()
    {
        if (!IsDualAxisLayout)
        {
            return;
        }

        IsDualAxisLayout = false;
        StatusRequested?.Invoke("历史趋势已切换为单轴布局。", null);
    }

    public void UseDualAxisLayout()
    {
        if (IsDualAxisLayout)
        {
            return;
        }

        IsDualAxisLayout = true;
        StatusRequested?.Invoke("历史趋势已切换为双轴布局。", null);
    }

    public void LoadFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        LoadDataset(new HistoricalTrendDataset(
            frames
                .SelectMany(frame => frame)
                .Where(snapshot => snapshot.Value is double value && !double.IsNaN(value) && !double.IsInfinity(value))
                .GroupBy(snapshot => snapshot.TagId)
                .Select(group =>
                {
                    var ordered = group.OrderBy(snapshot => snapshot.Timestamp).ToArray();
                    var first = ordered[0];
                    return new HistoricalTrendSeries(
                        first.TagId,
                        first.Name,
                        first.Address,
                        first.Unit,
                        ordered
                            .Select(snapshot => new HistoricalTrendPoint(
                                snapshot.Timestamp,
                                snapshot.Value!.Value,
                                snapshot.Quality,
                                snapshot.Source))
                            .ToArray());
                })
                .ToArray()));
        InitializeSeriesAxisRanges();
        SetSliderStateFromFrames(frames);
        SelectDefaultAxisSeries();
    }

    public void SetRangeTextFromFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        var timestamps = frames
            .Select(FrameTimestamp)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .Order()
            .ToArray();
        if (timestamps.Length == 0)
        {
            RangeStartText = string.Empty;
            RangeEndText = string.Empty;
            return;
        }

        RangeStartText = FormatTimestamp(timestamps[0]);
        RangeEndText = FormatTimestamp(timestamps[^1]);
    }

    public bool TryApplyRangeText(out string? error)
    {
        error = null;
        if (!TryParseOptionalTimestamp(RangeStartText, out var start, out error) ||
            !TryParseOptionalTimestamp(RangeEndText, out var end, out error))
        {
            return false;
        }

        if (!start.HasValue && !end.HasValue)
        {
            ResetVisibleTimeRange();
            if (VisibleTimeRange is { } visible)
            {
                ApplyViewport(visible.Start, visible.End, updateSliderValues: true);
            }

            return true;
        }

        if (start.HasValue && end.HasValue && start > end)
        {
            error = "历史趋势起始时间不能晚于结束时间。";
            return false;
        }

        var visibleStart = start ?? _totalStart ?? State.Dataset.Start;
        var visibleEnd = end ?? _totalEnd ?? State.Dataset.End;
        if (!visibleStart.HasValue || !visibleEnd.HasValue)
        {
            error = "当前没有可用的历史趋势时间范围。";
            return false;
        }

        SetVisibleTimeRange(visibleStart.Value, visibleEnd.Value);
        ApplyViewport(visibleStart.Value, visibleEnd.Value, updateSliderValues: true);
        return true;
    }

    public bool TryApplyYText(out string? error)
    {
        error = null;
        var hasMin = !string.IsNullOrWhiteSpace(YMinimumText);
        var hasMax = !string.IsNullOrWhiteSpace(YMaximumText);
        if (!hasMin && !hasMax)
        {
            ResetYRangeToFull();
            return true;
        }

        if (!hasMin || !hasMax ||
            !TryParseAxisValue(YMinimumText, out var min) ||
            !TryParseAxisValue(YMaximumText, out var max))
        {
            error = "请输入完整且有效的 Y 轴上下界。";
            return false;
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            error = "Y 轴上下界不能相同。";
            return false;
        }

        SetVisibleYRange(min, max);
        ApplyYRange(min, max, updateSliderValues: true);
        return true;
    }

    public void ResetYRangeToFull()
    {
        ResetLeftAxisRangeFromSelectedSeries();
        if (!IsYSliderEnabled)
        {
            return;
        }

        YRangeRequested?.Invoke(_totalYMinimum, _totalYMaximum);
    }

    public void ResetRightYRangeToFull()
    {
        ResetRightAxisRangeFromSelectedSeries();
        if (!IsYSliderEnabled)
        {
            return;
        }

        RightYRangeRequested?.Invoke(_totalRightYMinimum, _totalRightYMaximum);
    }

    public void SetVisibleTimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        State = _coordinator.SetVisibleTimeRange(State, start, end);
    }

    public void ResetVisibleTimeRange()
    {
        State = _coordinator.ResetVisibleTimeRange(State);
    }

    public void ResetTimeRangeToFull()
    {
        ResetVisibleTimeRange();
        if (VisibleTimeRange is not { } range)
        {
            return;
        }

        ApplyViewport(range.Start, range.End, updateSliderValues: true);
    }

    public bool TrySetVisibleDuration(TimeSpan duration, out string? error)
    {
        error = null;
        if (!_totalStart.HasValue || !_totalEnd.HasValue)
        {
            error = "当前没有可用的历史趋势时间范围。";
            return false;
        }

        var requested = duration < TimeSpan.FromSeconds(10)
            ? TimeSpan.FromSeconds(10)
            : duration;
        var fullRange = _totalEnd.Value - _totalStart.Value;
        if (requested >= fullRange)
        {
            ApplyViewport(_totalStart.Value, _totalEnd.Value, updateSliderValues: true);
            return true;
        }

        var end = VisibleTimeRange?.End ?? _totalEnd.Value;
        if (end > _totalEnd.Value || end <= _totalStart.Value)
        {
            end = _totalEnd.Value;
        }

        var start = end - requested;
        if (start < _totalStart.Value)
        {
            start = _totalStart.Value;
            end = start + requested;
            if (end > _totalEnd.Value)
            {
                end = _totalEnd.Value;
            }
        }

        ApplyViewport(start, end, updateSliderValues: true);
        StatusRequested?.Invoke($"历史趋势窗口已调整为 {FormatDuration(requested)}。", "历史趋势视图");
        return true;
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

    private void SetSliderStateFromFrames(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        _isUpdatingSliderState = true;
        try
        {
            var timeRange = GetTimestampRange(frames);
            IsViewportEnabled = timeRange is not null;
            if (timeRange is not null)
            {
                _totalStart = timeRange.Value.Start;
                _totalEnd = timeRange.Value.End > timeRange.Value.Start
                    ? timeRange.Value.End
                    : timeRange.Value.Start.AddMilliseconds(1);
                ViewportMinimum = AxisSliderMinimum;
                ViewportMaximum = AxisSliderMaximum;
                SetViewportSliderValues(AxisSliderMinimum, AxisSliderMaximum);
                UpdateViewportText(timeRange.Value.Start, timeRange.Value.End);
            }

            var yRange = GetValueRange(frames);
            IsYSliderEnabled = yRange is not null;
            if (yRange is not null)
            {
                var minimum = yRange.Value.Min;
                var maximum = yRange.Value.Max;
                if (minimum >= maximum)
                {
                    minimum -= 1d;
                    maximum += 1d;
                }

                var padding = Math.Max((maximum - minimum) * 0.05d, 1d);
                _totalYMinimum = minimum - padding;
                _totalYMaximum = maximum + padding;
                _totalRightYMinimum = _totalYMinimum;
                _totalRightYMaximum = _totalYMaximum;
                YSliderMinimum = AxisSliderMinimum;
                YSliderMaximum = AxisSliderMaximum;
                SetYSliderValues(AxisSliderMinimum, AxisSliderMaximum);
                SetRightYSliderValues(AxisSliderMinimum, AxisSliderMaximum);
                UpdateYRangeText(_totalYMinimum, _totalYMaximum);
                UpdateRightYRangeText(_totalRightYMinimum, _totalRightYMaximum);
            }
        }
        finally
        {
            _isUpdatingSliderState = false;
        }
    }

    private void ApplyViewportSliderChange()
    {
        if (_isUpdatingSliderState || !IsViewportEnabled || !_totalStart.HasValue || !_totalEnd.HasValue)
        {
            return;
        }

        var start = InterpolateTimestamp(_totalStart.Value, _totalEnd.Value, ViewportStart);
        var end = InterpolateTimestamp(_totalStart.Value, _totalEnd.Value, ViewportEnd);
        ApplyViewport(start, end, updateSliderValues: false);
        StatusRequested?.Invoke($"历史趋势视图已调整：{FormatTimestamp(start)} - {FormatTimestamp(end)}。", "历史趋势视图");
    }

    private void ApplyViewport(DateTimeOffset start, DateTimeOffset end, bool updateSliderValues)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        UpdateViewportText(start, end);
        SetVisibleTimeRange(start, end);
        if (updateSliderValues)
        {
            _isUpdatingSliderState = true;
            try
            {
                SetViewportSliderValues(TimestampToSliderValue(start), TimestampToSliderValue(end));
            }
            finally
            {
                _isUpdatingSliderState = false;
            }
        }

        ViewportRequested?.Invoke(start, end);
    }

    private void ApplyYSliderChange()
    {
        if (_isUpdatingSliderState || !IsYSliderEnabled)
        {
            return;
        }

        var min = InterpolateAxisValue(_totalYMinimum, _totalYMaximum, YLower);
        var max = InterpolateAxisValue(_totalYMinimum, _totalYMaximum, YUpper);
        SaveSeriesAxisRange(SelectedLeftAxisSeriesId, min, max, YLower, YUpper);
        ApplyYRange(min, max, updateSliderValues: false);
        StatusRequested?.Invoke(
            $"趋势 Y 轴范围已调整：{min.ToString("0.###", CultureInfo.InvariantCulture)} - {max.ToString("0.###", CultureInfo.InvariantCulture)}。",
            null);
    }

    private void ApplyRightYSliderChange()
    {
        if (_isUpdatingSliderState || !IsYSliderEnabled)
        {
            return;
        }

        var min = InterpolateAxisValue(_totalRightYMinimum, _totalRightYMaximum, RightYLower);
        var max = InterpolateAxisValue(_totalRightYMinimum, _totalRightYMaximum, RightYUpper);
        if (min > max)
        {
            (min, max) = (max, min);
        }

        SaveSeriesAxisRange(SelectedRightAxisSeriesId, min, max, RightYLower, RightYUpper);
        UpdateRightYRangeText(min, max);
        RightYRangeRequested?.Invoke(min, max);
        StatusRequested?.Invoke(
            $"趋势 Y2 轴范围已调整：{min.ToString("0.###", CultureInfo.InvariantCulture)} - {max.ToString("0.###", CultureInfo.InvariantCulture)}。",
            null);
    }

    private void ApplyYRange(double min, double max, bool updateSliderValues)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }

        UpdateYRangeText(min, max);
        SetVisibleYRange(min, max);
        if (updateSliderValues)
        {
            _isUpdatingSliderState = true;
            try
            {
                SetYSliderValues(AxisValueToSliderValue(min), AxisValueToSliderValue(max));
            }
            finally
            {
                _isUpdatingSliderState = false;
            }
        }

        YRangeRequested?.Invoke(min, max);
    }

    private void UpdateViewportText(DateTimeOffset start, DateTimeOffset end)
    {
        RangeStartText = FormatTimestamp(start);
        RangeEndText = FormatTimestamp(end);
        ViewportStartLabel = RangeStartText;
        ViewportEndLabel = RangeEndText;
    }

    private void UpdateYRangeText(double min, double max)
    {
        YMinimumText = min.ToString("0.###", CultureInfo.InvariantCulture);
        YMaximumText = max.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void UpdateRightYRangeText(double min, double max)
    {
        RightYMinimumText = min.ToString("0.###", CultureInfo.InvariantCulture);
        RightYMaximumText = max.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private void SetViewportSliderValues(double start, double end)
    {
        SetProperty(ref _viewportStart, start, nameof(ViewportStart));
        SetProperty(ref _viewportEnd, end, nameof(ViewportEnd));
    }

    private void SetYSliderValues(double lower, double upper)
    {
        SetProperty(ref _yLower, lower, nameof(YLower));
        SetProperty(ref _yUpper, upper, nameof(YUpper));
    }

    private void SetRightYSliderValues(double lower, double upper)
    {
        SetProperty(ref _rightYLower, lower, nameof(RightYLower));
        SetProperty(ref _rightYUpper, upper, nameof(RightYUpper));
    }

    private void SelectDefaultAxisSeries()
    {
        _isUpdatingSliderState = true;
        try
        {
            SelectedLeftAxisSeriesId = State.Dataset.Series.FirstOrDefault()?.SeriesId;
            SelectedRightAxisSeriesId = State.Dataset.Series.Skip(1).FirstOrDefault()?.SeriesId ?? SelectedLeftAxisSeriesId;
            ResetLeftAxisRangeFromSelectedSeries();
            ResetRightAxisRangeFromSelectedSeries();
        }
        finally
        {
            _isUpdatingSliderState = false;
        }
    }

    private void InitializeSeriesAxisRanges()
    {
        _seriesAxisRanges.Clear();
        foreach (var series in State.Dataset.Series)
        {
            var range = GetSeriesValueRange(series.SeriesId) ?? GetDatasetValueRange();
            if (range is null)
            {
                continue;
            }

            var (minimum, maximum) = ExpandRange(range.Value.Min, range.Value.Max);
            _seriesAxisRanges[series.SeriesId] = new SeriesAxisRangeState(
                minimum,
                maximum,
                minimum,
                maximum,
                AxisSliderMinimum,
                AxisSliderMaximum);
        }
    }

    private bool TryGetSeriesAxisRange(Guid? seriesId, out SeriesAxisRangeState rangeState)
    {
        if (seriesId.HasValue && _seriesAxisRanges.TryGetValue(seriesId.Value, out rangeState))
        {
            return true;
        }

        var fallback = GetDatasetValueRange();
        if (fallback is null)
        {
            rangeState = default;
            return false;
        }

        var (minimum, maximum) = ExpandRange(fallback.Value.Min, fallback.Value.Max);
        rangeState = new SeriesAxisRangeState(
            minimum,
            maximum,
            minimum,
            maximum,
            AxisSliderMinimum,
            AxisSliderMaximum);
        return true;
    }

    private void SaveSeriesAxisRange(
        Guid? seriesId,
        double visibleMinimum,
        double visibleMaximum,
        double lowerSlider,
        double upperSlider)
    {
        if (!seriesId.HasValue || !_seriesAxisRanges.TryGetValue(seriesId.Value, out var current))
        {
            return;
        }

        if (visibleMinimum > visibleMaximum)
        {
            (visibleMinimum, visibleMaximum) = (visibleMaximum, visibleMinimum);
        }

        _seriesAxisRanges[seriesId.Value] = current with
        {
            VisibleMinimum = visibleMinimum,
            VisibleMaximum = visibleMaximum,
            LowerSlider = ClampAxisSliderValue(lowerSlider, AxisSliderMinimum, AxisSliderMaximum),
            UpperSlider = ClampAxisSliderValue(upperSlider, AxisSliderMinimum, AxisSliderMaximum),
        };
    }

    private void ResetLeftAxisRangeFromSelectedSeries()
    {
        if (!TryGetSeriesAxisRange(SelectedLeftAxisSeriesId, out var rangeState))
        {
            YMinimumText = string.Empty;
            YMaximumText = string.Empty;
            return;
        }

        (_totalYMinimum, _totalYMaximum) = (rangeState.EngineeringMinimum, rangeState.EngineeringMaximum);
        UpdateYRangeText(rangeState.VisibleMinimum, rangeState.VisibleMaximum);
        SetYSliderValues(rangeState.LowerSlider, rangeState.UpperSlider);
        SetVisibleYRange(rangeState.VisibleMinimum, rangeState.VisibleMaximum);
        YRangeRequested?.Invoke(rangeState.VisibleMinimum, rangeState.VisibleMaximum);
    }

    private void ResetRightAxisRangeFromSelectedSeries()
    {
        if (!TryGetSeriesAxisRange(SelectedRightAxisSeriesId, out var rangeState))
        {
            RightYMinimumText = string.Empty;
            RightYMaximumText = string.Empty;
            return;
        }

        (_totalRightYMinimum, _totalRightYMaximum) = (rangeState.EngineeringMinimum, rangeState.EngineeringMaximum);
        UpdateRightYRangeText(rangeState.VisibleMinimum, rangeState.VisibleMaximum);
        SetRightYSliderValues(rangeState.LowerSlider, rangeState.UpperSlider);
        RightYRangeRequested?.Invoke(rangeState.VisibleMinimum, rangeState.VisibleMaximum);
    }

    private (double Min, double Max)? GetSeriesValueRange(Guid? seriesId)
    {
        if (!seriesId.HasValue)
        {
            return null;
        }

        var values = State.Dataset.Series
            .Where(series => series.SeriesId == seriesId.Value)
            .SelectMany(series => series.Points)
            .Select(point => point.Value)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        return values.Length == 0 ? null : (values.Min(), values.Max());
    }

    private (double Min, double Max)? GetDatasetValueRange()
    {
        var values = State.Dataset.Series
            .SelectMany(series => series.Points)
            .Select(point => point.Value)
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        return values.Length == 0 ? null : (values.Min(), values.Max());
    }

    private static (double Min, double Max) ExpandRange(double minimum, double maximum)
    {
        if (minimum >= maximum)
        {
            minimum -= 1d;
            maximum += 1d;
        }

        var padding = Math.Max((maximum - minimum) * 0.05d, 1d);
        return (minimum - padding, maximum + padding);
    }

    private double TimestampToSliderValue(DateTimeOffset timestamp)
    {
        if (!_totalStart.HasValue || !_totalEnd.HasValue)
        {
            return AxisSliderMinimum;
        }

        var totalMilliseconds = Math.Max(1d, (_totalEnd.Value - _totalStart.Value).TotalMilliseconds);
        var offsetMilliseconds = (timestamp - _totalStart.Value).TotalMilliseconds;
        return ClampAxisSliderValue(
            AxisSliderMinimum + (offsetMilliseconds / totalMilliseconds * AxisSliderMaximum),
            AxisSliderMinimum,
            AxisSliderMaximum);
    }

    private double AxisValueToSliderValue(double value)
    {
        var span = Math.Max(double.Epsilon, _totalYMaximum - _totalYMinimum);
        return ClampAxisSliderValue(
            AxisSliderMinimum + ((value - _totalYMinimum) / span * AxisSliderMaximum),
            AxisSliderMinimum,
            AxisSliderMaximum);
    }

    private static (DateTimeOffset Start, DateTimeOffset End)? GetTimestampRange(
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        var timestamps = frames
            .Select(FrameTimestamp)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToArray();
        return timestamps.Length == 0 ? null : (timestamps.Min(), timestamps.Max());
    }

    private static (double Min, double Max)? GetValueRange(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        var values = frames
            .SelectMany(frame => frame)
            .Select(snapshot => snapshot.Value)
            .OfType<double>()
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
            .ToArray();
        return values.Length == 0 ? null : (values.Min(), values.Max());
    }

    private static DateTimeOffset? FrameTimestamp(IReadOnlyList<PlcTagSnapshot> frame)
    {
        return frame.Count == 0 ? null : frame.Min(snapshot => snapshot.Timestamp);
    }

    private static bool TryParseOptionalTimestamp(
        string text,
        out DateTimeOffset? timestamp,
        out string? error)
    {
        timestamp = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed))
        {
            timestamp = parsed;
            return true;
        }

        error = $"无法识别时间：{text}";
        return false;
    }

    private static bool TryParseAxisValue(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{duration.TotalHours:0.#}h"
            : duration.TotalMinutes >= 1
                ? $"{duration.TotalMinutes:0.#}min"
                : $"{duration.TotalSeconds:0.#}s";
    }

    private static double ClampAxisSliderValue(double value, double minimum, double maximum)
    {
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static DateTimeOffset InterpolateTimestamp(DateTimeOffset start, DateTimeOffset end, double sliderValue)
    {
        var fraction = SliderFraction(sliderValue);
        var ticks = start.Ticks + (long)Math.Round((end.Ticks - start.Ticks) * fraction);
        return new DateTimeOffset(ticks, start.Offset);
    }

    private static double InterpolateAxisValue(double minimum, double maximum, double sliderValue)
    {
        return minimum + ((maximum - minimum) * SliderFraction(sliderValue));
    }

    private static double SliderFraction(double sliderValue)
    {
        return Math.Clamp(
            (sliderValue - AxisSliderMinimum) / (AxisSliderMaximum - AxisSliderMinimum),
            0d,
            1d);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

    private readonly record struct SeriesAxisRangeState(
        double EngineeringMinimum,
        double EngineeringMaximum,
        double VisibleMinimum,
        double VisibleMaximum,
        double LowerSlider,
        double UpperSlider);
}
