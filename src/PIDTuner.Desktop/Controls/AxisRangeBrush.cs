using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PIDTuner.Desktop.Controls;

public sealed class AxisRangeBrush : FrameworkElement
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(AxisRangeBrush),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(AxisRangeBrush),
        new FrameworkPropertyMetadata(1000d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue),
        typeof(double),
        typeof(AxisRangeBrush),
        new FrameworkPropertyMetadata(
            0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            OnRangeValueChanged));

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue),
        typeof(double),
        typeof(AxisRangeBrush),
        new FrameworkPropertyMetadata(
            1000d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            OnRangeValueChanged));

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(AxisRangeBrush),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double HorizontalTrackPadding = 10d;
    private const double VerticalTrackPadding = 10d;
    private const double HandleHitTolerance = 14d;

    private DragMode _dragMode = DragMode.None;
    private double _dragStartPointerValue;
    private double _dragStartLowerValue;
    private double _dragStartUpperValue;

    public AxisRangeBrush()
    {
        Focusable = true;
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return Orientation == Orientation.Horizontal
            ? new Size(Math.Min(240d, availableSize.Width), 34d)
            : new Size(38d, Math.Min(180d, availableSize.Height));
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        return ContainsPoint(point, ActualWidth, ActualHeight)
            ? new PointHitTestResult(this, point)
            : null;
    }

    private static bool ContainsPoint(Point point, double width, double height)
    {
        return point.X >= 0 && point.X <= width && point.Y >= 0 && point.Y <= height;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        CaptureMouse();
        var pointer = e.GetPosition(this);
        _dragMode = ResolveDragMode(pointer);
        _dragStartPointerValue = PointerToValue(pointer);
        _dragStartLowerValue = LowerValue;
        _dragStartUpperValue = UpperValue;
        SetDraggedValue(pointer);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsEnabled)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (!IsMouseCaptured)
        {
            Cursor = ResolveDragMode(e.GetPosition(this)) == DragMode.None ? Cursors.Arrow : Cursors.Hand;
            return;
        }

        if (_dragMode == DragMode.None)
        {
            return;
        }

        SetDraggedValue(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _dragMode = DragMode.None;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragMode = DragMode.None;
        base.OnLostMouseCapture(e);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var trackPen = new Pen(new SolidColorBrush(Color.FromRgb(156, 163, 175)), 1d);
        var selectedPen = new Pen(new SolidColorBrush(Color.FromRgb(37, 99, 235)), 4d)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(29, 78, 216)), 2d);
        var handleBrush = IsEnabled
            ? new SolidColorBrush(Color.FromRgb(239, 246, 255))
            : new SolidColorBrush(Color.FromRgb(229, 231, 235));

        if (Orientation == Orientation.Horizontal)
        {
            DrawHorizontal(drawingContext, trackPen, selectedPen, handlePen, handleBrush);
        }
        else
        {
            DrawVertical(drawingContext, trackPen, selectedPen, handlePen, handleBrush);
        }
    }

    private static void OnRangeValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        ((AxisRangeBrush)dependencyObject).InvalidateVisual();
    }

    private void DrawHorizontal(DrawingContext context, Pen trackPen, Pen selectedPen, Pen handlePen, Brush handleBrush)
    {
        var y = ActualHeight / 2d;
        var startX = HorizontalTrackPadding;
        var endX = Math.Max(startX, ActualWidth - HorizontalTrackPadding);
        var lowerX = ValueToHorizontalPosition(LowerValue);
        var upperX = ValueToHorizontalPosition(UpperValue);
        if (lowerX > upperX)
        {
            (lowerX, upperX) = (upperX, lowerX);
        }

        context.DrawLine(trackPen, new Point(startX, y), new Point(endX, y));
        context.DrawLine(selectedPen, new Point(lowerX, y), new Point(upperX, y));
        context.DrawEllipse(handleBrush, handlePen, new Point(lowerX, y), 6d, 10d);
        context.DrawEllipse(handleBrush, handlePen, new Point(upperX, y), 6d, 10d);
    }

    private void DrawVertical(DrawingContext context, Pen trackPen, Pen selectedPen, Pen handlePen, Brush handleBrush)
    {
        var x = ActualWidth / 2d;
        var topY = VerticalTrackPadding;
        var bottomY = Math.Max(topY, ActualHeight - VerticalTrackPadding);
        var lowerY = ValueToVerticalPosition(LowerValue);
        var upperY = ValueToVerticalPosition(UpperValue);
        var selectedTop = Math.Min(lowerY, upperY);
        var selectedBottom = Math.Max(lowerY, upperY);

        context.DrawLine(trackPen, new Point(x, topY), new Point(x, bottomY));
        context.DrawLine(selectedPen, new Point(x, selectedTop), new Point(x, selectedBottom));
        context.DrawEllipse(handleBrush, handlePen, new Point(x, upperY), 10d, 6d);
        context.DrawEllipse(handleBrush, handlePen, new Point(x, lowerY), 10d, 6d);
    }

    private DragMode ResolveDragMode(Point pointer)
    {
        var lowerPosition = Orientation == Orientation.Horizontal
            ? ValueToHorizontalPosition(LowerValue)
            : ValueToVerticalPosition(LowerValue);
        var upperPosition = Orientation == Orientation.Horizontal
            ? ValueToHorizontalPosition(UpperValue)
            : ValueToVerticalPosition(UpperValue);
        var pointerPosition = Orientation == Orientation.Horizontal ? pointer.X : pointer.Y;

        var lowerDistance = Math.Abs(pointerPosition - lowerPosition);
        var upperDistance = Math.Abs(pointerPosition - upperPosition);
        if (lowerDistance <= HandleHitTolerance || upperDistance <= HandleHitTolerance)
        {
            return upperDistance <= lowerDistance ? DragMode.UpperHandle : DragMode.LowerHandle;
        }

        if (IsPointerInsideSelectedRange(pointerPosition, lowerPosition, upperPosition))
        {
            return DragMode.Range;
        }

        return upperDistance <= lowerDistance ? DragMode.UpperHandle : DragMode.LowerHandle;
    }

    private void SetDraggedValue(Point pointer)
    {
        var value = PointerToValue(pointer);
        switch (_dragMode)
        {
            case DragMode.UpperHandle:
                UpperValue = value;
                break;
            case DragMode.LowerHandle:
                LowerValue = value;
                break;
            case DragMode.Range:
                MoveSelectedRange(value - _dragStartPointerValue);
                break;
        }
    }

    private void MoveSelectedRange(double delta)
    {
        var lower = Math.Min(_dragStartLowerValue, _dragStartUpperValue);
        var upper = Math.Max(_dragStartLowerValue, _dragStartUpperValue);
        var span = upper - lower;
        var requestedLower = lower + delta;
        var requestedUpper = upper + delta;
        if (requestedLower < Minimum)
        {
            requestedLower = Minimum;
            requestedUpper = requestedLower + span;
        }

        if (requestedUpper > Maximum)
        {
            requestedUpper = Maximum;
            requestedLower = requestedUpper - span;
        }

        LowerValue = Clamp(requestedLower, Minimum, Maximum);
        UpperValue = Clamp(requestedUpper, Minimum, Maximum);
    }

    private double PointerToValue(Point pointer)
    {
        return Orientation == Orientation.Horizontal
            ? HorizontalPositionToValue(pointer.X)
            : VerticalPositionToValue(pointer.Y);
    }

    private static bool IsPointerInsideSelectedRange(double pointerPosition, double lowerPosition, double upperPosition)
    {
        var start = Math.Min(lowerPosition, upperPosition);
        var end = Math.Max(lowerPosition, upperPosition);
        return pointerPosition >= start && pointerPosition <= end;
    }

    private double ValueToHorizontalPosition(double value)
    {
        var startX = HorizontalTrackPadding;
        var endX = Math.Max(startX, ActualWidth - HorizontalTrackPadding);
        return startX + Normalize(value) * (endX - startX);
    }

    private double ValueToVerticalPosition(double value)
    {
        var topY = VerticalTrackPadding;
        var bottomY = Math.Max(topY, ActualHeight - VerticalTrackPadding);
        return bottomY - Normalize(value) * (bottomY - topY);
    }

    private double HorizontalPositionToValue(double x)
    {
        var startX = HorizontalTrackPadding;
        var endX = Math.Max(startX, ActualWidth - HorizontalTrackPadding);
        var ratio = (Clamp(x, startX, endX) - startX) / Math.Max(1d, endX - startX);
        return Denormalize(ratio);
    }

    private double VerticalPositionToValue(double y)
    {
        var topY = VerticalTrackPadding;
        var bottomY = Math.Max(topY, ActualHeight - VerticalTrackPadding);
        var ratio = (bottomY - Clamp(y, topY, bottomY)) / Math.Max(1d, bottomY - topY);
        return Denormalize(ratio);
    }

    private double Normalize(double value)
    {
        var span = Math.Max(double.Epsilon, Maximum - Minimum);
        return Clamp((value - Minimum) / span, 0d, 1d);
    }

    private double Denormalize(double ratio)
    {
        return Minimum + Clamp(ratio, 0d, 1d) * Math.Max(0d, Maximum - Minimum);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    private enum DragMode
    {
        None,
        LowerHandle,
        UpperHandle,
        Range,
    }
}
