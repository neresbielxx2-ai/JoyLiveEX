using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VirtualJoyCon.ControllerModel;

namespace VirtualJoyCon.DesktopApp.Controls;

/// <summary>
/// Interactive analog stick: press-and-drag (mouse or touch), the knob follows the
/// pointer clamped to the travel radius, and it snaps back to center on release.
/// Emits raw normalized vectors (-1..1, Y down positive); the hub applies deadzone,
/// sensitivity, curve and inversion so numbers match the spec examples.
/// </summary>
public sealed class StickControl : FrameworkElement
{
    public bool IsLeft { get; init; }
    public Action<bool, float, float>? OnStick { get; set; }

    private double _knobX, _knobY;     // visual offset from center, normalized -1..1
    private bool _dragging;
    private readonly double _size;

    public bool IsDragging => _dragging;
    public (float X, float Y) RawPosition => ((float)_knobX, (float)_knobY);

    public StickControl(double size = 112)
    {
        _size = size;
        Focusable = false;
        Cursor = Cursors.Hand;
    }

    protected override Size MeasureOverride(Size availableSize) => new(_size, _size);

    /// <summary>External echo (keyboard/gamepad/phone-driven) — no capture involved.</summary>
    public void SetVisualPosition(float x, float y)
    {
        if (_dragging) return;
        if (Math.Abs(_knobX - x) < 0.01 && Math.Abs(_knobY - y) < 0.01) return;
        _knobX = Math.Clamp(x, -1, 1);
        _knobY = Math.Clamp(y, -1, 1);
        InvalidateVisual();
    }

    private void UpdateFrom(Point p)
    {
        double cx = ActualWidth / 2, cy = ActualHeight / 2;
        double dx = (p.X - cx) / (ActualWidth / 2);
        double dy = (p.Y - cy) / (ActualHeight / 2);
        double mag = Math.Sqrt(dx * dx + dy * dy);
        if (mag > 1) { dx /= mag; dy /= mag; }
        _knobX = dx; _knobY = dy;
        OnStick?.Invoke(IsLeft, (float)dx, (float)dy);
        InvalidateVisual();
    }

    // ---- mouse ----
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        UpdateFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed && _dragging) UpdateFrom(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    // ---- touch ----
    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        _dragging = true;
        CaptureTouch(e.TouchDevice);
        UpdateFrom(e.GetTouchPoint(this).Position);
        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        base.OnTouchMove(e);
        if (_dragging) UpdateFrom(e.GetTouchPoint(this).Position);
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        ReleaseTouchCapture(e.TouchDevice);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnTouchLostCapture(TouchEventArgs e)
    {
        base.OnTouchLostCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _knobX = 0; _knobY = 0;              // return to center on release (spec §4)
        OnStick?.Invoke(IsLeft, 0, 0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0) return;
        var well = (Brush)FindResource("StickWell");
        var ring = (Brush)FindResource("Rail");
        var knob = (Brush)FindResource(_dragging ? "Accent" : "StickKnob");

        double outer = Math.Min(w, h) / 2 - 2;
        double knobR = outer * 0.46;
        Point center = new(w / 2, h / 2);

        dc.DrawEllipse(well, new Pen(ring, 2), center, outer, outer);
        var knobCenter = new Point(center.X + _knobX * (outer - knobR), center.Y + _knobY * (outer - knobR));
        dc.DrawEllipse(knob, new Pen(ring, 1.5), knobCenter, knobR, knobR);
        // inner grip circle
        dc.DrawEllipse(well, null, knobCenter, knobR * 0.55, knobR * 0.55);
    }
}
