using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PIDTuner.Desktop.Controls;

public partial class DateTimeSegmentPicker : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(DateTimeOffset),
        typeof(DateTimeSegmentPicker),
        new FrameworkPropertyMetadata(
            DateTimeOffset.Now,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(DateTimeOffset),
        typeof(DateTimeSegmentPicker),
        new FrameworkPropertyMetadata(DateTimeOffset.MinValue, OnLimitChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(DateTimeOffset),
        typeof(DateTimeSegmentPicker),
        new FrameworkPropertyMetadata(DateTimeOffset.MaxValue, OnLimitChanged));

    private const double PixelsPerStep = 10d;

    private Border? _dragSource;
    private DateTimeOffset _dragOriginValue;
    private double _dragOriginY;
    private int _lastDragSteps;

    public DateTimeSegmentPicker()
    {
        InitializeComponent();
        RefreshSegments();
    }

    public DateTimeOffset Value
    {
        get => (DateTimeOffset)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public DateTimeOffset Minimum
    {
        get => (DateTimeOffset)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public DateTimeOffset Maximum
    {
        get => (DateTimeOffset)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((DateTimeSegmentPicker)dependencyObject).RefreshSegments();
    }

    private static void OnLimitChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var picker = (DateTimeSegmentPicker)dependencyObject;
        picker.CoerceValue(ValueProperty);
        picker.RefreshSegments();
    }

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        var picker = (DateTimeSegmentPicker)dependencyObject;
        var value = (DateTimeOffset)baseValue;
        var minimum = picker.Minimum;
        var maximum = picker.Maximum < minimum ? minimum : picker.Maximum;
        return value < minimum
            ? minimum
            : value > maximum
                ? maximum
                : value;
    }

    private void Segment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || !TryGetSegment(border, out _))
        {
            return;
        }

        _dragSource = border;
        _dragOriginValue = Value.ToLocalTime();
        _dragOriginY = e.GetPosition(this).Y;
        _lastDragSteps = 0;
        border.Focus();
        border.CaptureMouse();
        SetActiveAppearance(border, true);
        e.Handled = true;
    }

    private void Segment_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border border || border != _dragSource || !border.IsMouseCaptured ||
            e.LeftButton != MouseButtonState.Pressed || !TryGetSegment(border, out var segment))
        {
            return;
        }

        var steps = (int)Math.Truncate((_dragOriginY - e.GetPosition(this).Y) / PixelsPerStep);
        if (steps == _lastDragSteps)
        {
            return;
        }

        _lastDragSteps = steps;
        SetCurrentValue(ValueProperty, Clamp(Adjust(_dragOriginValue, segment, steps)));
        e.Handled = true;
    }

    private void Segment_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border == _dragSource)
        {
            EndDrag(border);
            e.Handled = true;
        }
    }

    private void Segment_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Border border && border == _dragSource)
        {
            EndDrag(border);
        }
    }

    private void Segment_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Border border || !TryGetSegment(border, out var segment))
        {
            return;
        }

        var steps = e.Delta > 0 ? 1 : -1;
        SetCurrentValue(ValueProperty, Clamp(Adjust(Value.ToLocalTime(), segment, steps)));
        e.Handled = true;
    }

    private void Segment_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Border border || !TryGetSegment(border, out var segment))
        {
            return;
        }

        var steps = e.Key switch
        {
            Key.Up => 1,
            Key.Down => -1,
            Key.PageUp => 10,
            Key.PageDown => -10,
            _ => 0,
        };
        if (steps == 0)
        {
            return;
        }

        SetCurrentValue(ValueProperty, Clamp(Adjust(Value.ToLocalTime(), segment, steps)));
        e.Handled = true;
    }

    private void EndDrag(Border border)
    {
        SetActiveAppearance(border, false);
        if (border.IsMouseCaptured)
        {
            border.ReleaseMouseCapture();
        }

        _dragSource = null;
        _lastDragSteps = 0;
    }

    private static void SetActiveAppearance(Border border, bool isActive)
    {
        if (isActive)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(204, 251, 241));
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 184, 166));
            return;
        }

        border.ClearValue(Border.BackgroundProperty);
        border.ClearValue(Border.BorderBrushProperty);
    }

    private DateTimeOffset Clamp(DateTimeOffset value)
    {
        var minimum = Minimum;
        var maximum = Maximum < minimum ? minimum : Maximum;
        return value < minimum
            ? minimum
            : value > maximum
                ? maximum
                : value;
    }

    private static DateTimeOffset Adjust(DateTimeOffset value, DateTimeSegment segment, int steps)
    {
        if (steps == 0)
        {
            return value;
        }

        try
        {
            return segment switch
            {
                DateTimeSegment.Year => value.AddYears(steps),
                DateTimeSegment.Month => value.AddMonths(steps),
                DateTimeSegment.Day => value.AddDays(steps),
                DateTimeSegment.Hour => value.AddHours(steps),
                DateTimeSegment.Minute => value.AddMinutes(steps),
                DateTimeSegment.Second => value.AddSeconds(steps),
                _ => value,
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return steps > 0 ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue;
        }
    }

    private static bool TryGetSegment(FrameworkElement element, out DateTimeSegment segment)
    {
        return Enum.TryParse(element.Tag?.ToString(), out segment);
    }

    private void RefreshSegments()
    {
        if (YearText is null)
        {
            return;
        }

        var local = Value.ToLocalTime();
        YearText.Text = local.Year.ToString("0000");
        MonthText.Text = local.Month.ToString("00");
        DayText.Text = local.Day.ToString("00");
        HourText.Text = local.Hour.ToString("00");
        MinuteText.Text = local.Minute.ToString("00");
        SecondText.Text = local.Second.ToString("00");
    }

    private enum DateTimeSegment
    {
        Year,
        Month,
        Day,
        Hour,
        Minute,
        Second,
    }
}
