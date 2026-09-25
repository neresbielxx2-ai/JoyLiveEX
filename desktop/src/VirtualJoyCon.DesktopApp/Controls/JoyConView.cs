using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VirtualJoyCon.Configuration;
using VirtualJoyCon.ControllerModel;

namespace VirtualJoyCon.DesktopApp.Controls;

/// <summary>
/// Builds the two visually-separated virtual Joy-Cons entirely in code
/// (modern dark gray rails, circular buttons, big analogs, cross d-pad, +/−,
/// HOME, L/R/ZL/ZR, rail SL/SR, stick clicks) — an original look inspired by
/// the requested reference, no third-party logos or trade dress.
/// </summary>
public static class JoyConView
{
    public const double RailW = 236;
    public const double RailH = 500;

    public readonly record struct ControllerRefs(
        List<VirtualButton> Buttons,
        StickControl Left,
        StickControl Right,
        FrameworkElement Root);

    public static ControllerRefs Build(InterfaceSettings iface, Action<uint, bool> onButton, Action<bool, float, float> onStick)
    {
        var buttons = new List<VirtualButton>();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iface.RailSpacing) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = BuildLeft(buttons, onButton, out var leftStick);
        var right = BuildRight(buttons, onButton, out var rightStick);
        left.Margin = new Thickness(0, 0, 0, 0);
        right.Margin = new Thickness(0, 0, 0, 0);
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);

        var root = new Border
        {
            Child = new Viewbox { Child = grid, Stretch = Stretch.Uniform },
            Opacity = Math.Clamp(iface.Opacity, 0.25, 1.0),
        };
        return new ControllerRefs(buttons, leftStick, rightStick, root);
    }

    // ------------------------------------------------------------------
    private static Canvas BuildLeft(List<VirtualButton> buttons, Action<uint, bool> onButton, out StickControl stick)
    {
        var c = NewRail(new CornerRadius(104, 18, 18, 104));

        // top shoulders: ZL behind, L front
        Add(c, buttons, onButton, (uint)ButtonFlags.ZL, "ZL", VButtonKind.Pill, 74, 24, 84, 2, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.L, "L", VButtonKind.Pill, 74, 20, 84, 12, 11);
        // minus
        Add(c, buttons, onButton, (uint)ButtonFlags.Minus, "—", VButtonKind.Pill, 40, 14, 158, 74, 12);
        // stick (top area)
        stick = new StickControl { IsLeft = true, Width = 116, Height = 116 };
        Canvas.SetLeft(stick, 58);
        Canvas.SetTop(stick, 116);
        c.Children.Add(stick);
        // stick click + capture
        Add(c, buttons, onButton, (uint)ButtonFlags.LeftStick, "L3", VButtonKind.Circle, 30, 30, 168, 208, 9);
        Add(c, buttons, onButton, (uint)ButtonFlags.Capture, "◻", VButtonKind.Square, 26, 26, 168, 258, 11);
        // d-pad cross at bottom
        double dx = 68, dy = 312;
        Add(c, buttons, onButton, (uint)ButtonFlags.DPadUp, "▲", VButtonKind.DPadV, 44, 50, dx + 26, dy, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.DPadDown, "▼", VButtonKind.DPadV, 44, 50, dx + 26, dy + 86, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.DPadLeft, "◀", VButtonKind.DPadH, 50, 44, dx, dy + 30, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.DPadRight, "▶", VButtonKind.DPadH, 50, 44, dx + 86, dy + 30, 11);
        var hub = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = (Brush)Application.Current.FindResource("ButtonBrush"),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(hub, dx + 36);
        Canvas.SetTop(hub, dy + 40);
        c.Children.Add(hub);
        // rail side buttons SL/SR
        Add(c, buttons, onButton, (uint)ButtonFlags.SL_Left, "SL", VButtonKind.Square, 20, 52, 6, 150, 8);
        Add(c, buttons, onButton, (uint)ButtonFlags.SR_Left, "SR", VButtonKind.Square, 20, 52, 6, 214, 8);
        AddCaption(c, "LEFT JOY-CON", 74, 470);
        return c;
    }

    private static Canvas BuildRight(List<VirtualButton> buttons, Action<uint, bool> onButton, out StickControl stick)
    {
        var c = NewRail(new CornerRadius(18, 104, 104, 18));

        Add(c, buttons, onButton, (uint)ButtonFlags.ZR, "ZR", VButtonKind.Pill, 74, 24, 78, 2, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.R, "R", VButtonKind.Pill, 74, 20, 78, 12, 11);
        Add(c, buttons, onButton, (uint)ButtonFlags.Plus, "+", VButtonKind.Pill, 40, 14, 38, 74, 12);
        // face cluster diamond (ABXY)
        double cx = 126, cy = 128, d = 50;
        Add(c, buttons, onButton, (uint)ButtonFlags.X, "X", VButtonKind.Circle, 46, 46, cx - 23, cy - d - 23, 15, Color.FromRgb(0x64, 0xB5, 0xF6));
        Add(c, buttons, onButton, (uint)ButtonFlags.B, "B", VButtonKind.Circle, 46, 46, cx + d - 23, cy - 23, 15, Color.FromRgb(0xE5, 0x73, 0x73));
        Add(c, buttons, onButton, (uint)ButtonFlags.A, "A", VButtonKind.Circle, 46, 46, cx - 23, cy + d - 23, 15, Color.FromRgb(0xEF, 0x53, 0x50));
        Add(c, buttons, onButton, (uint)ButtonFlags.Y, "Y", VButtonKind.Circle, 46, 46, cx - d - 23, cy - 23, 15, Color.FromRgb(0x81, 0xC7, 0x84));
        // stick (lower area)
        stick = new StickControl { IsLeft = false, Width = 116, Height = 116 };
        Canvas.SetLeft(stick, 30);
        Canvas.SetTop(stick, 230);
        c.Children.Add(stick);
        Add(c, buttons, onButton, (uint)ButtonFlags.RightStick, "R3", VButtonKind.Circle, 30, 30, 168, 248, 9);
        // home
        Add(c, buttons, onButton, (uint)ButtonFlags.Home, "◎", VButtonKind.Circle, 34, 34, 170, 306, 13);
        // rails
        Add(c, buttons, onButton, (uint)ButtonFlags.SL_Right, "SL", VButtonKind.Square, 20, 52, 210, 150, 8);
        Add(c, buttons, onButton, (uint)ButtonFlags.SR_Right, "SR", VButtonKind.Square, 20, 52, 210, 214, 8);
        AddCaption(c, "RIGHT JOY-CON", 66, 470);
        return c;
    }

    private static Canvas NewRail(CornerRadius radius)
    {
        var c = new Canvas { Width = RailW, Height = RailH, Background = Brushes.Transparent };
        var body = new Border
        {
            Width = RailW,
            Height = RailH,
            CornerRadius = radius,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.FindResource("Rail"),
            Background = new LinearGradientBrush(
                (Color)Application.Current.FindResource("CardColor"),
                Color.FromRgb(0x22, 0x26, 0x2E), new Point(0, 0), new Point(0, 1)),
        };
        c.Children.Add(body);
        return c;
    }

    private static void Add(Canvas c, List<VirtualButton> all, Action<uint, bool> onButton,
        uint flag, string glyphText, VButtonKind kind, double w, double h, double x, double y,
        double glyphSize, Color? glyphColor = null)
    {
        var b = new VirtualButton
        {
            Flag = flag,
            Glyph = glyphText,
            Kind = kind,
            GlyphColor = glyphColor,
            OnChanged = onButton,
            Width = w,
            Height = h,
        };
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        c.Children.Add(b);
        all.Add(b);
        _ = glyphSize; // glyph font size is derived from shape size inside VirtualButton.OnRender
    }

    private static void AddCaption(Canvas c, string text, double x, double y)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Muted"),
        };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        c.Children.Add(t);
    }
}
