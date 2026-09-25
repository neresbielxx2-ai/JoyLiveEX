using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VirtualJoyCon.DesktopApp.Controls;

public enum VButtonKind { Circle, Pill, Square, DPadH, DPadV }

/// <summary>
/// Fully interactive virtual button: pointer-down = DOWN, pointer-up/lost-capture
/// = UP (no "stuck held" states — capture loss always forces release).
/// Drawn in code so mouse AND touch work identically. External sources (keyboard,
/// physical gamepad) can light it via SetVisualPressed for feedback.
/// </summary>
public sealed class VirtualButton : FrameworkElement
{
    public uint Flag { get; init; }
    public string Glyph { get; set; } = "";
    public VButtonKind Kind { get; init; } = VButtonKind.Circle;
    public Color? GlyphColor { get; set; }
    public Action<uint, bool>? OnChanged { get; set; }
    public FontFamily Font { get; set; } = new("Segoe UI");

    private bool _pointerHeld;
    private bool _visualPressed;

    public bool IsPressedState => _pointerHeld || _visualPressed;

    public VirtualButton()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Hand;
    }

    protected override Size MeasureOverride(Size availableSize) => new(Width, Height);

    public void SetVisualPressed(bool p)
    {
        if (_visualPressed == p) return;
        _visualPressed = p;
        InvalidateVisual();
    }

    private void SetHeld(bool down)
    {
        if (_pointerHeld == down) return;
        _pointerHeld = down;
        OnChanged?.Invoke(Flag, down);
        Dispatcher.InvokeAsync(InvalidateVisual, DispatcherPriority.Send);
    }

    // ---- mouse path ----
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        SetHeld(true);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        SetHeld(false);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        SetHeld(false); // anti-stuck: capture stolen (alt-tab, popup)
    }

    // ---- touch path (per-finger isolation: each control captures its own touch) ----
    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        CaptureTouch(e.TouchDevice);
        SetHeld(true);
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        ReleaseTouchCapture(e.TouchDevice);
        SetHeld(false);
        e.Handled = true;
    }

    protected override void OnTouchLostCapture(TouchEventArgs e)
    {
        base.OnTouchLostCapture(e);
        SetHeld(false); // anti-stuck on swipe-away / system cancel
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        bool pressed = IsPressedState;

        var bg = (Brush)FindResource(pressed ? "ButtonPressed" : "ButtonBrush");
        var outline = (Brush)FindResource("Rail");
        var textBrush = pressed ? Brushes.White : ((GlyphColor ?? Color.FromRgb(0xE8, 0xEA, 0xF0))).ToBrush();

        switch (Kind)
        {
            case VButtonKind.Circle:
            {
                double r = Math.Min(w, h) / 2;
                dc.DrawEllipse(bg, new Pen(outline, 1.2), new Point(w / 2, h / 2), r - 1, r - 1);
                break;
            }
            case VButtonKind.Pill:
            {
                double radius = Math.Min(w, h) / 2.4;
                dc.DrawRoundedRectangle(bg, new Pen(outline, 1.2), new Rect(0, 0, w, h), radius, radius);
                break;
            }
            case VButtonKind.Square:
            {
                double radius = Math.Min(w, h) / 4;
                dc.DrawRoundedRectangle(bg, new Pen(outline, 1.2), new Rect(0, 0, w, h), radius, radius);
                break;
            }
            case VButtonKind.DPadH:
            {
                dc.DrawRoundedRectangle(bg, new Pen(outline, 1), new Rect(0, h * 0.22, w, h * 0.56), 8, 8);
                break;
            }
            case VButtonKind.DPadV:
            {
                dc.DrawRoundedRectangle(bg, new Pen(outline, 1), new Rect(w * 0.22, 0, w * 0.56, h), 8, 8);
                break;
            }
        }

        if (!string.IsNullOrEmpty(Glyph))
        {
            var ft = new FormattedText(Glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                Kind == VButtonKind.Circle ? Math.Min(w, h) * 0.42 : Math.Min(w, h) * 0.40,
                textBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = w + 40,
            };
            dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 - ft.Height / 2));
        }
    }
}

internal static class ColorEx
{
    public static Brush ToBrush(this Color c) => new SolidColorBrush(c);
}
