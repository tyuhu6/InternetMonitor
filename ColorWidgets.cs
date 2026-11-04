using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace InternetMonitor;

internal static class ColorMath
{
    public static Color HsvToColor(double hue, double saturation, double value)
    {
        double h = ((hue % 360) + 360) % 360;
        double s = Math.Clamp(saturation, 0, 1);
        double v = Math.Clamp(value, 0, 1);

        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;

        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static (double Hue, double Saturation, double Value) ColorToHsv(Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = 0;
        if (delta > 0.00001)
        {
            if (max == r)
                hue = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);
        }

        if (hue < 0)
            hue += 360;

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>饱和度 / 明度取色方块，鼠标按下并拖动即可选色。</summary>
public sealed class SatValBox : FrameworkElement
{
    private const double CornerRadius = 8;

    private static readonly Pen ThumbOuter = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)), 1.2));
    private static readonly Pen ThumbInner = Frozen(new Pen(Brushes.White, 1.8));

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    public SatValBox()
    {
        Cursor = Cursors.Cross;
        ClipToBounds = true;
    }

    public double Hue
    {
        get => _hue;
        set { _hue = value; InvalidateVisual(); }
    }

    public double Saturation
    {
        get => _saturation;
        set { _saturation = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public event EventHandler? Picked;

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (rect.Width < 8 || rect.Height < 8)
            return;

        dc.PushClip(new RectangleGeometry(rect, CornerRadius, CornerRadius));
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, ColorMath.HsvToColor(_hue, 1, 1), 0), null, rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), null, rect);
        dc.Pop();

        double x = Math.Clamp(_saturation * rect.Width, 7, rect.Width - 7);
        double y = Math.Clamp((1 - _value) * rect.Height, 7, rect.Height - 7);
        var center = new Point(x, y);

        dc.DrawEllipse(null, ThumbOuter, center, 6.6, 6.6);
        dc.DrawEllipse(null, ThumbInner, center, 5.2, 5.2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Sample(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
            Sample(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    private void Sample(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        Saturation = Math.Clamp(point.X / ActualWidth, 0, 1);
        Value = 1 - Math.Clamp(point.Y / ActualHeight, 0, 1);
        Picked?.Invoke(this, EventArgs.Empty);
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}

/// <summary>横向色相条。</summary>
public sealed class HueBar : FrameworkElement
{
    private static readonly LinearGradientBrush Rainbow = CreateRainbow();
    private static readonly Pen ThumbOuter = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), 1.1));
    private static readonly Pen ThumbInner = Frozen(new Pen(Brushes.White, 2.2));

    private double _hue;

    public HueBar()
    {
        Cursor = Cursors.Hand;
        ClipToBounds = true;
    }

    public double Hue
    {
        get => _hue;
        set { _hue = value; InvalidateVisual(); }
    }

    public event EventHandler? HueChanged;

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        if (rect.Width < 8 || rect.Height < 4)
            return;

        double radius = rect.Height / 2;
        dc.DrawRoundedRectangle(Rainbow, null, rect, radius, radius);

        double x = Math.Clamp(_hue / 360 * rect.Width, 3, rect.Width - 3);
        var thumb = new Rect(x - 3, -1, 6, rect.Height + 2);
        dc.DrawRoundedRectangle(Brushes.White, ThumbOuter, thumb, 3, 3);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Sample(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
            Sample(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    private void Sample(Point point)
    {
        if (ActualWidth <= 0)
            return;

        Hue = Math.Clamp(point.X / ActualWidth, 0, 1) * 360;
        HueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static LinearGradientBrush CreateRainbow()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x3B, 0x30), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD6, 0x0A), 1.0 / 6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3D, 0xD6, 0x68), 2.0 / 6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x22, 0xD3, 0xEE), 3.0 / 6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4C, 0x9A, 0xFF), 4.0 / 6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xA7, 0x8B, 0xFA), 5.0 / 6));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x3B, 0x30), 1.0));
        brush.Freeze();
        return brush;
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
