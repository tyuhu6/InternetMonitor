using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using DrawingColor = System.Drawing.Color;

namespace InternetMonitor;

internal static class AppIcons
{
    /// <summary>托盘图标：深色圆角底 + 蓝/橙细线箭头，深浅任务栏下都清晰。</summary>
    public static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(DrawingColor.Transparent);

            using var background = RoundedRect(new RectangleF(1.2f, 1.2f, 29.6f, 29.6f), 8.5f);
            using var backgroundBrush = new SolidBrush(DrawingColor.FromArgb(0x10, 0x13, 0x18));
            g.FillPath(backgroundBrush, background);

            DrawArrow(g, DrawingColor.FromArgb(0x4C, 0x86, 0xFF), 9.8f, 6.8f, 21.2f);
            DrawArrow(g, DrawingColor.FromArgb(0xF2, 0xA3, 0x3C), 22.2f, 25.2f, 10.8f);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DrawArrow(Graphics g, DrawingColor color, float x, float fromY, float toY)
    {
        using var pen = new Pen(color, 2.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        g.DrawLine(pen, x, fromY, x, toY);

        float direction = Math.Sign(toY - fromY);
        float tipY = toY;
        float wingY = toY - direction * 3f;

        g.DrawLines(pen, new[]
        {
            new PointF(x - 3.2f, wingY),
            new PointF(x, tipY),
            new PointF(x + 3.2f, wingY)
        });
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
