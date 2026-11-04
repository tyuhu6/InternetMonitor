using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace InternetMonitor;

/// <summary>某一天的流量累计（按本地日期）。</summary>
public sealed class DailyUsage
{
    public string Date { get; set; } = "";
    public ulong Rx { get; set; }
    public ulong Tx { get; set; }
}

public sealed class AppState
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool Topmost { get; set; } = true;
    public bool ShowMainWindow { get; set; } = true;
    public bool ShowTaskbarSpeed { get; set; } = true;
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontScale { get; set; } = 1.0;
    public string BgColor { get; set; } = "#FFFFFF";
    public string TextColor { get; set; } = "#101318";
    public string DownColor { get; set; } = "#2F6BFF";
    public string UpColor { get; set; } = "#E8890C";
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>最近 40 天的流量记录，用于今日/本周/本月统计。</summary>
    public List<DailyUsage> History { get; set; } = new();

    // 旧版本字段，仅用于迁移到 History
    public string Date { get; set; } = "";
    public ulong TodayRx { get; set; }
    public ulong TodayTx { get; set; }

    public double PeakRx { get; set; }
    public double PeakTx { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InternetMonitor",
        "state.json");

    public static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath));
                if (state != null)
                    return state;
            }
        }
        catch (Exception)
        {
            // 配置损坏时使用默认值
        }

        return new AppState();
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // 写入失败不影响监控功能
        }
    }
}
