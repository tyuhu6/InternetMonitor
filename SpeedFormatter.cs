using System;

namespace InternetMonitor;

/// <summary>网速显示单位。</summary>
public enum SpeedUnitMode
{
    /// <summary>字节单位，自动进位：B/s、KB/s、MB/s。</summary>
    Auto = 0,

    /// <summary>字节单位，简短写法：B/s、K/s、M/s。</summary>
    Compact = 1,

    /// <summary>比特单位，1000 进制：bps、Kbps、Mbps、Gbps。</summary>
    Bits = 2
}

public static class SpeedFormatter
{
    public static SpeedUnitMode Parse(string? value) => value switch
    {
        nameof(SpeedUnitMode.Compact) => SpeedUnitMode.Compact,
        nameof(SpeedUnitMode.Bits) => SpeedUnitMode.Bits,
        _ => SpeedUnitMode.Auto
    };

    public static (string Value, string Unit) Format(double bytesPerSecond, SpeedUnitMode mode)
    {
        double value = Math.Max(0, bytesPerSecond);

        if (mode == SpeedUnitMode.Bits)
        {
            double bits = value * 8;

            if (bits < 1000)
                return (bits.ToString("0"), "bps");
            if (bits < 1000d * 1000)
                return ((bits / 1000).ToString("0.0"), "Kbps");
            if (bits < 1000d * 1000 * 1000)
                return ((bits / (1000d * 1000)).ToString("0.00"), "Mbps");
            return ((bits / (1000d * 1000 * 1000)).ToString("0.00"), "Gbps");
        }

        if (mode == SpeedUnitMode.Compact)
        {
            if (value < 1024)
                return (value.ToString("0"), "B/s");
            if (value < 1024d * 1024)
                return ((value / 1024).ToString("0.#"), "K/s");
            if (value < 1024d * 1024 * 1024)
                return ((value / (1024d * 1024)).ToString("0.#"), "M/s");
            return ((value / (1024d * 1024 * 1024)).ToString("0.#"), "G/s");
        }

        if (value < 1024)
            return (value.ToString("0"), "B/s");
        if (value < 1024d * 1024)
            return ((value / 1024).ToString("0.0"), "KB/s");
        if (value < 1024d * 1024 * 1024)
            return ((value / (1024d * 1024)).ToString("0.00"), "MB/s");
        return ((value / (1024d * 1024 * 1024)).ToString("0.00"), "GB/s");
    }
}
