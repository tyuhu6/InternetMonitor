using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace InternetMonitor;

/// <summary>近 60 秒上/下行速率曲线。</summary>
public sealed class SpeedChart : FrameworkElement
{
    private const int Capacity = 60;

    private readonly double[] _down = new double[Capacity];
    private readonly double[] _up = new double[Capacity];
    private int _count;
    private int _head;

    private Pen _gridPen = new Pen(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), 1);
    private Pen _baselinePen = new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 1);
    private Pen _downPen = new Pen(new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)), 1.6);
    private Pen _upPen = new Pen(new SolidColorBrush(Color.FromRgb(0xF0, 0xA6, 0x3A)), 1.4);
    private Brush _downFill = Brushes.Transparent;
    private Brush _upFill = Brushes.Transparent;

    public SpeedChart()
    {
        ApplyColors(new Appearance());
    }

    public void ApplyColors(Appearance appearance)
    {
        var down = Appearance.Solid(appearance.Down);
        var up = Appearance.Solid(appearance.Up);

        _gridPen = Frozen(new Pen(Appearance.Solid(Color.FromArgb(
            (byte)(appearance.IsDark ? 0x0C : 0x12),
            appearance.IsDark ? (byte)0xFF : (byte)0x00,
            appearance.IsDark ? (byte)0xFF : (byte)0x00,
            appearance.IsDark ? (byte)0xFF : (byte)0x00)), 1));
        _baselinePen = Frozen(new Pen(Appearance.Solid(Color.FromArgb(
            (byte)(appearance.IsDark ? 0x1C : 0x18),
            appearance.IsDark ? (byte)0xFF : (byte)0x00,
            appearance.IsDark ? (byte)0xFF : (byte)0x00,
            appearance.IsDark ? (byte)0xFF : (byte)0x00)), 1));
        _downPen = Frozen(new Pen(down, 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });
        _upPen = Frozen(new Pen(up, 1.4)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });
        _downFill = Fill(appearance.Down, 0x3A);
        _upFill = Fill(appearance.Up, 0x24);

        InvalidateVisual();
    }

    public void AddSample(double downBytesPerSec, double upBytesPerSec)
    {
        _down[_head] = downBytesPerSec;
        _up[_head] = upBytesPerSec;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity)
            _count++;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1)
            return;

        for (int i = 1; i < 4; i++)
        {
            double y = Math.Round(h * i / 4) + 0.5;
            dc.DrawLine(_gridPen, new Point(0, y), new Point(w, y));
        }

        double baseline = h - 0.5;
        dc.DrawLine(_baselinePen, new Point(0, baseline), new Point(w, baseline));

        if (_count < 2)
            return;

        double max = 0;
        for (int i = 0; i < _count; i++)
        {
            if (_down[i] > max) max = _down[i];
            if (_up[i] > max) max = _up[i];
        }

        if (max <= 0)
            return;

        max = NiceCeil(max);

        var downPoints = BuildPoints(_down, w, h, max);
        var upPoints = BuildPoints(_up, w, h, max);
        double bottom = h - 2;

        dc.DrawGeometry(_downFill, null, BuildCurve(downPoints, bottom));
        dc.DrawGeometry(_upFill, null, BuildCurve(upPoints, bottom));
        dc.DrawGeometry(null, _downPen, BuildCurve(downPoints, double.NaN));
        dc.DrawGeometry(null, _upPen, BuildCurve(upPoints, double.NaN));

        var last = downPoints[^1];
        dc.DrawEllipse(_downPen.Brush, null, last, 2.4, 2.4);
    }

    private List<Point> BuildPoints(double[] buffer, double w, double h, double max)
    {
        const double padX = 5;
        const double padTop = 6;
        const double padBottom = 3;

        double usableW = Math.Max(1, w - padX * 2);
        double usableH = Math.Max(1, h - padTop - padBottom);
        double stepX = usableW / (Capacity - 1);

        int start = (_head - _count + Capacity) % Capacity;
        var points = new List<Point>(_count);

        for (int i = 0; i < _count; i++)
        {
            double v = buffer[(start + i) % Capacity];
            double x = padX + stepX * i;
            double y = padTop + usableH - Math.Min(v, max) / max * usableH;
            points.Add(new Point(x, y));
        }

        return points;
    }

    private static StreamGeometry BuildCurve(IReadOnlyList<Point> points, double closeAtY)
    {
        bool fill = !double.IsNaN(closeAtY);
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], fill, fill);

            for (int i = 1; i < points.Count - 1; i++)
            {
                var mid = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                ctx.QuadraticBezierTo(points[i], mid, true, false);
            }

            ctx.LineTo(points[^1], true, false);

            if (fill)
            {
                ctx.LineTo(new Point(points[^1].X, closeAtY), false, false);
                ctx.LineTo(new Point(points[0].X, closeAtY), false, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static double NiceCeil(double value)
    {
        if (value <= 0)
            return 1;

        double exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double mantissa = value / exponent;
        double nice = mantissa <= 1 ? 1 : mantissa <= 2 ? 2 : mantissa <= 5 ? 5 : 10;
        return nice * exponent;
    }

    private static Brush Fill(Color color, byte topAlpha)
    {
        var brush = new LinearGradientBrush(
            Color.FromArgb(topAlpha, color.R, color.G, color.B),
            Color.FromArgb(0x00, color.R, color.G, color.B),
            new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
