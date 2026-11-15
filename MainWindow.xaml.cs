using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace InternetMonitor;

public partial class MainWindow : Window
{
    private const int SaveEveryTicks = 10;
    private const int HistoryDays = 40;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly AppState _state;
    private readonly WinForms.NotifyIcon _tray;
    private WinForms.ToolStripMenuItem _topMostItem = null!;
    private WinForms.ToolStripMenuItem _showMainItem = null!;
    private WinForms.ToolStripMenuItem _taskbarItem = null!;

    private Appearance _appearance = new();
    private TaskbarSpeedWindow? _taskbar;
    private SettingsWindow? _settings;
    private DailyUsage _today = new();
    private bool _syncingMenu;
    private bool _keepTrayMenuOpen;

    private ulong _prevRx;
    private ulong _prevTx;
    private long _prevTicks;
    private int _tickCount;
    private DateTime _day;
    private bool _trayTipShown;
    private double _lastRxSpeed;
    private double _lastTxSpeed;

    public AppState State => _state;

    public MainWindow()
    {
        InitializeComponent();

        Icon = new BitmapImage(new Uri("pack://application:,,,/app.ico"));

        _state = AppState.Load();
        _day = DateTime.Today;

        MigrateHistory();
        _today = GetOrCreateToday();
        TrimHistory();

        RefreshFromState();
        ApplyAppearance();

        _tray = CreateTray();
        _tray.MouseDoubleClick += (_, _) => ToggleVisibility();

        // 显示器插拔/分辨率变化时，把窗口拉回新的可见区域
        SystemEvents.DisplaySettingsChanged += (_, _) => KeepWindowOnScreen();

        Loaded += (_, _) =>
        {
            PlaceWindow();
            StartMonitoring();

            // Show() 尚未结束时直接 Hide() 会被覆盖，等它结束后再应用显示设置
            Dispatcher.BeginInvoke(new Action(ApplyVisibility), DispatcherPriority.Send);
        };

        LocationChanged += (_, _) =>
        {
            if (IsLoaded)
            {
                _state.Left = Left;
                _state.Top = Top;
            }
        };

        Closing += OnClosingHandler;
        Closed += (_, _) => System.Windows.Application.Current.Shutdown();
    }

    /// <summary>重新读取外观配置并立即应用到所有窗口。</summary>
    public void ApplyAppearance()
    {
        _appearance = Appearance.FromState(_state);
        _appearance.Apply();

        Chart.ApplyColors(_appearance);
        Chart.Height = Math.Round(72 * _state.FontScale);
        Width = Math.Round(420 * _state.FontScale);

        Topmost = _state.Topmost;
        UpdatePinVisual();

        _taskbar?.ApplyAppearance(_appearance);
        _taskbar?.SetSpeedUnit(SpeedFormatter.Parse(_state.SpeedUnit));
        RefreshUsage();

        if (IsLoaded)
            SetPosition(Left, Top);
    }

    public void SetMainWindowVisible(bool visible)
    {
        _state.ShowMainWindow = visible;

        if (visible)
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }
        else
        {
            Hide();
        }

        SyncTrayMenu();
        _settings?.SyncFromState();
        SaveStateNow();
    }

    public void SetTaskbarVisible(bool visible)
    {
        _state.ShowTaskbarSpeed = visible;

        if (visible)
        {
            if (_taskbar == null)
            {
                _taskbar = new TaskbarSpeedWindow();
                _taskbar.ApplyAppearance(_appearance);
                _taskbar.SetSpeedUnit(SpeedFormatter.Parse(_state.SpeedUnit));
            }

            _taskbar.SetEnabled(true);
            _taskbar.SetSpeed(_lastRxSpeed, _lastTxSpeed);
        }
        else
        {
            _taskbar?.SetEnabled(false);
        }

        SyncTrayMenu();
        _settings?.SyncFromState();
        _state.Save();
    }

    public void SetTopmost(bool value)
    {
        _state.Topmost = value;
        Topmost = value;

        if (_settings != null)
            _settings.Topmost = value;

        UpdatePinVisual();
        SyncTrayMenu();
        _settings?.SyncFromState();
        _state.Save();
    }

    public void SaveStateNow()
    {
        // 窗口未显示过时 Left/Top 为 NaN，序列化会失败，跳过坐标即可
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _state.Left = Left;
            _state.Top = Top;
        }

        _state.Save();
    }

    private void ApplyVisibility()
    {
        if (!_state.ShowMainWindow)
            Hide();

        if (_state.ShowTaskbarSpeed)
            SetTaskbarVisible(true);
    }

    private void StartMonitoring()
    {
        var (rx, tx) = NetworkStatsReader.ReadTotals();
        _prevRx = rx;
        _prevTx = tx;
        _prevTicks = _clock.ElapsedMilliseconds;

        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var (rx, tx) = NetworkStatsReader.ReadTotals();
        long now = _clock.ElapsedMilliseconds;
        double seconds = Math.Max(0.001, (now - _prevTicks) / 1000.0);

        ulong deltaRx = rx >= _prevRx ? rx - _prevRx : 0;
        ulong deltaTx = tx >= _prevTx ? tx - _prevTx : 0;
        _prevRx = rx;
        _prevTx = tx;
        _prevTicks = now;

        double rxSpeed = deltaRx / seconds;
        double txSpeed = deltaTx / seconds;
        _lastRxSpeed = rxSpeed;
        _lastTxSpeed = txSpeed;

        RolloverDayIfNeeded();

        _today.Rx += deltaRx;
        _today.Tx += deltaTx;
        if (rxSpeed > _state.PeakRx)
            _state.PeakRx = rxSpeed;
        if (txSpeed > _state.PeakTx)
            _state.PeakTx = txSpeed;

        var (rxValue, rxUnit) = FormatSpeed(rxSpeed);
        var (txValue, txUnit) = FormatSpeed(txSpeed);

        ShowSpeed(rxValue, rxUnit, txValue, txUnit);

        Chart.AddSample(rxSpeed, txSpeed);
        RefreshUsage();

        if (_state.ShowTaskbarSpeed)
            _taskbar?.SetSpeed(rxSpeed, txSpeed);

        if (++_tickCount % SaveEveryTicks == 0)
            SaveStateNow();
    }

    /// <summary>把上下行数值写进界面，同时更新峰值和托盘提示。</summary>
    private void ShowSpeed(string rxValue, string rxUnit, string txValue, string txUnit)
    {
        RxValue.Text = rxValue;
        RxUnit.Text = rxUnit;
        TxValue.Text = txValue;
        TxUnit.Text = txUnit;

        var (peakRxValue, peakRxUnit) = FormatSpeed(_state.PeakRx);
        var (peakTxValue, peakTxUnit) = FormatSpeed(_state.PeakTx);
        PeakText.Text = $"峰值 ↓ {peakRxValue} {peakRxUnit}   ↑ {peakTxValue} {peakTxUnit}";

        string tip = $"网络监控  ↓ {rxValue} {rxUnit}  ↑ {txValue} {txUnit}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    /// <summary>切换单位后立即按新单位重画，不用等下一次采集。</summary>
    public void ApplySpeedUnit()
    {
        var mode = SpeedFormatter.Parse(_state.SpeedUnit);

        _taskbar?.SetSpeedUnit(mode);

        var (rxValue, rxUnit) = SpeedFormatter.Format(_lastRxSpeed, mode);
        var (txValue, txUnit) = SpeedFormatter.Format(_lastTxSpeed, mode);
        ShowSpeed(rxValue, rxUnit, txValue, txUnit);
    }

    private void RolloverDayIfNeeded()
    {
        if (DateTime.Today == _day)
            return;

        _day = DateTime.Today;
        _today = GetOrCreateToday();
        TrimHistory();

        _state.PeakRx = 0;
        _state.PeakTx = 0;
    }

    private void RefreshFromState()
    {
        var (peakRxValue, peakRxUnit) = FormatSpeed(_state.PeakRx);
        var (peakTxValue, peakTxUnit) = FormatSpeed(_state.PeakTx);
        PeakText.Text = $"峰值 ↓ {peakRxValue} {peakRxUnit}   ↑ {peakTxValue} {peakTxUnit}";

        RefreshUsage();
    }

    private void RefreshUsage()
    {
        var week = SumDays(7);
        var month = SumMonth();

        TodayRxText.Text = FormatBytes(_today.Rx);
        TodayTxText.Text = FormatBytes(_today.Tx);
        WeekRxText.Text = FormatBytes(week.Rx);
        WeekTxText.Text = FormatBytes(week.Tx);
        MonthRxText.Text = FormatBytes(month.Rx);
        MonthTxText.Text = FormatBytes(month.Tx);

        Bars.Update(_appearance, _state.History, DateTime.Today);
    }

    private (ulong Rx, ulong Tx) SumDays(int days)
    {
        string from = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");
        string to = DateTime.Today.ToString("yyyy-MM-dd");

        ulong rx = 0;
        ulong tx = 0;

        foreach (var entry in _state.History)
        {
            if (string.CompareOrdinal(entry.Date, from) < 0 || string.CompareOrdinal(entry.Date, to) > 0)
                continue;

            rx += entry.Rx;
            tx += entry.Tx;
        }

        return (rx, tx);
    }

    private (ulong Rx, ulong Tx) SumMonth()
    {
        string prefix = DateTime.Today.ToString("yyyy-MM");

        ulong rx = 0;
        ulong tx = 0;

        foreach (var entry in _state.History)
        {
            if (!entry.Date.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            rx += entry.Rx;
            tx += entry.Tx;
        }

        return (rx, tx);
    }

    private void MigrateHistory()
    {
        if (_state.History.Count == 0 &&
            (_state.TodayRx > 0 || _state.TodayTx > 0) &&
            !string.IsNullOrEmpty(_state.Date))
        {
            _state.History.Add(new DailyUsage
            {
                Date = _state.Date,
                Rx = _state.TodayRx,
                Tx = _state.TodayTx
            });
        }

        _state.TodayRx = 0;
        _state.TodayTx = 0;
        _state.Date = "";
    }

    private DailyUsage GetOrCreateToday()
    {
        string key = DateTime.Today.ToString("yyyy-MM-dd");

        foreach (var entry in _state.History)
        {
            if (entry.Date == key)
                return entry;
        }

        var created = new DailyUsage { Date = key };
        _state.History.Add(created);
        return created;
    }

    private void TrimHistory()
    {
        string cutoff = DateTime.Today.AddDays(-(HistoryDays - 1)).ToString("yyyy-MM-dd");
        _state.History.RemoveAll(entry => string.CompareOrdinal(entry.Date, cutoff) < 0);
        _state.History.Sort((a, b) => string.CompareOrdinal(a.Date, b.Date));
    }

    private void PlaceWindow()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double left = _state.Left ?? (SystemParameters.WorkArea.Right - width - 24);
        double top = _state.Top ?? (SystemParameters.WorkArea.Top + 72);
        SetPosition(left, top);
    }

    private void SetPosition(double left, double top)
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;

        double minLeft = SystemParameters.VirtualScreenLeft + 8;
        double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - width - 8;
        double minTop = SystemParameters.VirtualScreenTop + 8;
        double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - height - 8;

        Left = Math.Max(minLeft, Math.Min(left, maxLeft));
        Top = Math.Max(minTop, Math.Min(top, maxTop));
    }

    /// <summary>
    /// 显示器插拔后，VirtualScreen 范围变化，窗口可能跑到屏幕外。
    /// 把它拉回新的虚拟屏幕可见区域内。
    /// </summary>
    private void KeepWindowOnScreen()
    {
        if (!IsLoaded)
            return;

        // 等布局刷新完成（DPI 变化可能导致 ActualWidth/Height 改变）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // 先让系统更新 SystemParameters
            SystemParameters.WorkArea.Equals(null);

            double width = ActualWidth > 0 ? ActualWidth : Width;
            double height = ActualHeight > 0 ? ActualHeight : Height;

            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vRight = vLeft + SystemParameters.VirtualScreenWidth;
            double vBottom = vTop + SystemParameters.VirtualScreenHeight;

            // 检查窗口是否至少还有一部分在屏幕内
            bool visibleHorizontally = Left + width > vLeft + 8 && Left < vRight - 8;
            bool visibleVertically = Top + height > vTop + 8 && Top < vBottom - 8;

            if (!visibleHorizontally || !visibleVertically)
            {
                // 完全不可见（比如之前在第二显示器上），放到主屏幕工作区右上角
                double newLeft = SystemParameters.WorkArea.Right - width - 24;
                double newTop = SystemParameters.WorkArea.Top + 72;
                SetPosition(newLeft, newTop);
            }
            else
            {
                // 还有一部分可见，只是越界了，用 SetPosition 限制一下
                SetPosition(Left, Top);
            }

            SaveStateNow();
        }), DispatcherPriority.Input);
    }

    private WinForms.NotifyIcon CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Opening += (_, _) => SyncTrayMenu();

        // 勾选类菜单项点完保持展开，直到用户点到别处
        menu.Closing += (_, args) =>
        {
            if (args.CloseReason == WinForms.ToolStripDropDownCloseReason.ItemClicked && _keepTrayMenuOpen)
                args.Cancel = true;

            _keepTrayMenuOpen = false;
        };

        _showMainItem = new WinForms.ToolStripMenuItem("显示主窗口")
        {
            CheckOnClick = true,
            Checked = _state.ShowMainWindow
        };
        _showMainItem.MouseDown += (_, _) => _keepTrayMenuOpen = true;
        _showMainItem.CheckedChanged += (_, _) =>
        {
            if (_syncingMenu)
                return;

            if (_showMainItem.Checked)
                SetMainWindowVisible(true);
            else
                HideToTray();
        };

        _taskbarItem = new WinForms.ToolStripMenuItem("任务栏显示网速")
        {
            CheckOnClick = true,
            Checked = _state.ShowTaskbarSpeed
        };
        _taskbarItem.MouseDown += (_, _) => _keepTrayMenuOpen = true;
        _taskbarItem.CheckedChanged += (_, _) =>
        {
            if (!_syncingMenu)
                SetTaskbarVisible(_taskbarItem.Checked);
        };

        _topMostItem = new WinForms.ToolStripMenuItem("窗口始终置顶")
        {
            CheckOnClick = true,
            Checked = _state.Topmost
        };
        _topMostItem.MouseDown += (_, _) => _keepTrayMenuOpen = true;
        _topMostItem.CheckedChanged += (_, _) =>
        {
            if (!_syncingMenu)
                SetTopmost(_topMostItem.Checked);
        };

        var settingsItem = new WinForms.ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => ShowSettings();

        var exitItem = new WinForms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Close();

        menu.Items.Add(_showMainItem);
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(_topMostItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return new WinForms.NotifyIcon
        {
            Icon = AppIcons.CreateTrayIcon(),
            Text = "网络监控",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private void SyncTrayMenu()
    {
        _syncingMenu = true;
        _showMainItem.Checked = IsVisible;
        _taskbarItem.Checked = _state.ShowTaskbarSpeed;
        _topMostItem.Checked = _state.Topmost;
        _syncingMenu = false;
    }

    private void ShowSettings()
    {
        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(this);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
    }

    private void UpdatePinVisual()
    {
        PinButton.Foreground = new SolidColorBrush(Topmost
            ? _appearance.Down
            : _appearance.Secondary);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标提前松开等情况下会抛异常，忽略即可
        }

        KeepWindowReachable();
        SaveStateNow();
    }

    /// <summary>拖动后保证窗口始终有一部分留在屏幕内，避免拖丢。</summary>
    private void KeepWindowReachable()
    {
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;

        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        double bottom = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;
        const double reachable = 90;

        if (Left + width < left + reachable)
            Left = left + reachable - width;
        if (Left > right - reachable)
            Left = right - reachable;
        if (Top + height < top + reachable)
            Top = top + reachable - height;
        if (Top > bottom - reachable)
            Top = bottom - reachable;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void Pin_Click(object sender, RoutedEventArgs e) => SetTopmost(!Topmost);

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    /// <summary>关闭按钮只收起窗口，程序继续在托盘运行。</summary>
    private void HideToTray()
    {
        Hide();
        SyncTrayMenu();

        if (_trayTipShown)
            return;

        _trayTipShown = true;
        _tray.ShowBalloonTip(2500, "网络监控",
            "已在后台继续监控，双击托盘图标可重新打开；右键托盘图标可退出。",
            WinForms.ToolTipIcon.None);
    }

    private void ShowWindow()
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            SetMainWindowVisible(true);
            return;
        }

        Activate();
    }

    private void ToggleVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
            HideToTray();
        else
            ShowWindow();
    }

    private void OnClosingHandler(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        SaveStateNow();
        _tray.Visible = false;
        _tray.Dispose();
        _taskbar?.Close();
        _settings?.Close();
    }

    private (string Value, string Unit) FormatSpeed(double bytesPerSecond) =>
        SpeedFormatter.Format(bytesPerSecond, SpeedFormatter.Parse(_state.SpeedUnit));

    private static string FormatBytes(ulong bytes)
    {
        double value = bytes;

        if (value < 1024)
            return value.ToString("0") + " B";
        if (value < 1024d * 1024)
            return (value / 1024).ToString("0.0") + " KB";
        if (value < 1024d * 1024 * 1024)
            return (value / (1024d * 1024)).ToString("0.0") + " MB";
        if (value < 1024d * 1024 * 1024 * 1024)
            return (value / (1024d * 1024 * 1024)).ToString("0.00") + " GB";
        return (value / (1024d * 1024 * 1024 * 1024)).ToString("0.00") + " TB";
    }
}
