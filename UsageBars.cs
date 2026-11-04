using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace InternetMonitor;

/// <summary>最近 7 天流量柱状图：下方为下载，上方为上传。</summary>
public sealed class UsageBars : FrameworkElement
{
    private const int Days = 7;

    private readonly (ulong Rx, ulong Tx)[] _values = new (ulong, ulong)[Days];
    private Brush _downBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
    private Brush _upBrush = new SolidColorBrush(Color.FromRgb(0xF0, 0xA6, 0x3A));
    private Brush _emptyBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    public void Update(Appearance appearance, IReadOnlyList<DailyUsage> history, DateTime today)
    {
        _downBrush = Appearance.Solid(appearance.Down);
        _upBrush = Appearance.Solid(appearance.Up);
        _emptyBrush = Appearance.Solid(appearance.IsDark
            ? Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x16, 0x00, 0x00, 0x00));

        for (int i = 0; i < Days; i++)
        {
            string key = today.AddDays(i - (Days - 1)).ToString("yyyy-MM-dd");
            ulong rx = 0;
            ulong tx = 0;

            for (int j = 0; j < history.Count; j++)
            {
                if (history[j].Date != key)
                    continue;

                rx = history[j].Rx;
                tx = history[j].Tx;
                break;
            }

            _values[i] = (rx, tx);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 20 || h < 8)
            return;

        double max = 0;
        foreach (var (rx, tx) in _values)
            max = Math.Max(max, (double)rx + tx);

        double slot = w / Days;
        double barWidth = Math.Clamp(slot * 0.34, 6, 18);
        double baseline = h - 0.5;

        for (int i = 0; i < Days; i++)
        {
            var (rx, tx) = _values[i];
            double total = rx + tx;
            double x = slot * i + (slot - barWidth) / 2;

            if (total <= 0 || max <= 0)
            {
                dc.DrawRoundedRectangle(_emptyBrush, null,
                    new Rect(x, baseline - 3, barWidth, 3), 1.5, 1.5);
                continue;
            }

            double barHeight = Math.Max(4, total / max * (h - 1));
            double top = baseline - barHeight;

            dc.DrawRoundedRectangle(_downBrush, null,
                new Rect(x, top, barWidth, barHeight), 3, 3);

            double upHeight = barHeight * tx / total;
            if (upHeight >= 1)
            {
                dc.DrawRoundedRectangle(_upBrush, null,
                    new Rect(x, top, barWidth, Math.Max(2, upHeight)), 3, 3);
            }
        }
    }
}
