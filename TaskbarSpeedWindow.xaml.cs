using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace InternetMonitor;

/// <summary>
/// 贴在任务栏通知区域左侧的极简网速读数，鼠标可穿透。
/// 通过 SetParent 把自己变成 Shell_TrayWnd 的子窗口，
/// 这样 Z 序自动跟随任务栏（开始菜单期间也不会消失）。
/// </summary>
public partial class TaskbarSpeedWindow : Window
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    // 16ms 贴近 60 帧，任务栏自动隐藏时逐帧跟随；空闲时只有几次 GetWindowRect，不占 CPU
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    // 背景明暗取色间隔，换壁纸/切主题后 0.25 秒内就能跟上
    private static readonly TimeSpan ContrastSampleInterval = TimeSpan.FromMilliseconds(250);

    private Appearance _appearance = new();
    private SpeedUnitMode _unit = SpeedUnitMode.Auto;
    private bool _taskbarIsDark = true;
    private bool _enabled;
    private bool _layoutDirty;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _lastLeft = double.NaN;
    private double _lastTop = double.NaN;
    private double _lastRx;
    private double _lastTx;
    private IntPtr _handle;
    private IntPtr _trayWnd;   // Shell_TrayWnd 句柄
    private IntPtr _trayNotifyWnd;  // TrayNotifyWnd 句柄（通知区，用来算锚点）
    private bool _isAttached;  // 是否已成功 SetParent 到任务栏
    private DateTime _lastScaleProbe = DateTime.MinValue;
    private DateTime _lastContrastSample = DateTime.MinValue;
    private DateTime _lastAttachAttempt = DateTime.MinValue;

    public TaskbarSpeedWindow()
    {
        InitializeComponent();
        ApplyAppearance(new Appearance());

        _followTimer.Tick += (_, _) => Reposition();

        // 显示器插拔/分辨率变化时，重置所有缓存让 Reposition 立即重新探测
        SystemEvents.DisplaySettingsChanged += (_, _) => InvalidateLayoutCache();
    }

    private void InvalidateLayoutCache()
    {
        _lastScaleProbe = DateTime.MinValue;
        _trayNotifyWnd = IntPtr.Zero;
        _isAttached = false;
        _trayWnd = IntPtr.Zero;
        _lastLeft = double.NaN;
        _lastTop = double.NaN;

        if (IsVisible)
            Reposition();
    }

    /// <summary>启用后持续跟随任务栏位置。</summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;

        _enabled = enabled;

        if (enabled)
        {
            Reposition();
            _followTimer.Start();
        }
        else
        {
            _followTimer.Stop();
            _lastLeft = double.NaN;
            _lastTop = double.NaN;

            if (IsVisible)
                Hide();
        }
    }

    public void ApplyAppearance(Appearance appearance)
    {
        _appearance = appearance;
        FontFamily = new FontFamily(appearance.FontFamilyName);

        double size = 11 * Math.Clamp(appearance.FontScale, 0.85, 1.35);
        DownText.FontSize = size;
        UpText.FontSize = size;

        double iconSize = Math.Round(size * 0.82);
        DownIconBox.Width = iconSize;
        DownIconBox.Height = iconSize;
        UpIconBox.Width = iconSize;
        UpIconBox.Height = iconSize;

        _layoutDirty = true;
        ApplyColors();
    }

    /// <summary>按任务栏实际明暗决定文字与强调色，浅色任务栏也不会看不清。</summary>
    private void ApplyColors()
    {
        var text = Appearance.Solid(_taskbarIsDark
            ? Color.FromRgb(0xF2, 0xF4, 0xF7)
            : Color.FromRgb(0x1A, 0x1D, 0x23));

        DownText.Foreground = text;
        UpText.Foreground = text;
        DownIcon.Stroke = Appearance.Solid(_taskbarIsDark
            ? Appearance.Shift(_appearance.Down, 0.35)
            : _appearance.Down);
        UpIcon.Stroke = Appearance.Solid(_taskbarIsDark
            ? Appearance.Shift(_appearance.Up, 0.30)
            : _appearance.Up);
    }

    /// <summary>截取任务栏空白处的一小块像素，判断它到底是深色还是浅色。</summary>
    private void SampleTaskbarBrightness(int physicalX, int physicalY, int width, int height)
    {
        if (DateTime.UtcNow - _lastContrastSample < ContrastSampleInterval)
            return;

        _lastContrastSample = DateTime.UtcNow;

        int sampleX = physicalX - 100;
        if (sampleX < 0)
            sampleX = physicalX + width + 16;

        try
        {
            using var bitmap = new System.Drawing.Bitmap(32, 4);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(sampleX, physicalY + Math.Max(0, height / 2 - 2), 0, 0, bitmap.Size);

            double total = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    total += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                }
            }

            double luminance = total / (bitmap.Width * bitmap.Height) / 255;

            // 迟滞判断：亮度落在中间地带时保持当前配色，避免壁纸渐变/过渡动画中来回闪
            bool dark = _taskbarIsDark ? luminance < 0.58 : luminance < 0.42;
            if (dark == _taskbarIsDark)
                return;

            _taskbarIsDark = dark;
            ApplyColors();
        }
        catch (Exception)
        {
            // 截屏失败时保持当前配色
        }
    }

    public void SetSpeed(double rxBytesPerSec, double txBytesPerSec)
    {
        _lastRx = rxBytesPerSec;
        _lastTx = txBytesPerSec;
        ShowSpeed();
    }

    /// <summary>切换单位后立刻按新单位重画。</summary>
    public void SetSpeedUnit(SpeedUnitMode mode)
    {
        if (_unit == mode)
            return;

        _unit = mode;
        ShowSpeed();
    }

    private void ShowSpeed()
    {
        var (rxValue, rxUnit) = SpeedFormatter.Format(_lastRx, _unit);
        var (txValue, txUnit) = SpeedFormatter.Format(_lastTx, _unit);

        DownText.Text = rxValue + rxUnit;
        UpText.Text = txValue + txUnit;
        _layoutDirty = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _handle = new WindowInteropHelper(this).Handle;

        // 设置扩展样式：分层窗口 + 工具窗口（不在 Alt+Tab 里）+ 不抢焦点 + 鼠标穿透
        int exStyle = GetWindowLong(_handle, GWL_EXSTYLE);
        SetWindowLong(_handle, GWL_EXSTYLE, exStyle | (int)WS_EX_LAYERED | (int)WS_EX_TOOLWINDOW | (int)WS_EX_NOACTIVATE | (int)WS_EX_TRANSPARENT);
    }

    /// <summary>
    /// 把窗口变成 Shell_TrayWnd 的子窗口。
    /// 这样 Z 序自动跟随任务栏，开始菜单期间也不会消失。
    /// </summary>
    private bool TryAttach()
    {
        if (_handle == IntPtr.Zero)
            return false;

        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero)
            return false;

        // 验证句柄确实是 Shell_TrayWnd（任务栏被重建后旧句柄可能失效）
        var className = new StringBuilder(64);
        if (GetClassName(tray, className, className.Capacity) == 0 || className.ToString() != "Shell_TrayWnd")
            return false;

        if (_isAttached && _trayWnd == tray)
        {
            // 验证父窗口确实是我们的预期
            if (GetParent(_handle) == tray)
                return true;
            _isAttached = false;
        }

        // 改窗口基础样式：从 WS_POPUP 改成 WS_CHILD
        // WPF WindowStyle=None+AllowsTransparency=True 默认是 WS_POPUP
        int oldStyle = GetWindowLong(_handle, GWL_STYLE);
        uint newStyle = (uint)oldStyle & ~WS_POPUP;  // 去掉 popup
        newStyle |= WS_CHILD;   // 加上 child
        SetWindowLong(_handle, GWL_STYLE, (int)newStyle);

        // SetParent 到 Shell_TrayWnd
        SetParent(_handle, tray);

        // 验证成功
        if (GetParent(_handle) != tray)
            return false;

        _trayWnd = tray;
        _trayNotifyWnd = IntPtr.Zero;  // 重新找通知区
        _isAttached = true;

        return true;
    }

    /// <summary>确保窗口已挂到任务栏，失败时定期重试。</summary>
    private void EnsureAttached()
    {
        if (_isAttached && _trayWnd != IntPtr.Zero && IsWindow(_trayWnd))
        {
            // 验证父窗口还是 Shell_TrayWnd
            if (GetParent(_handle) != _trayWnd)
            {
                _isAttached = false;
                _trayWnd = IntPtr.Zero;
            }
        }

        if (!_isAttached)
        {
            // 限频重试：explorer 重启后 Shell_TrayWnd 会消失再重建
            if ((DateTime.UtcNow - _lastAttachAttempt).TotalSeconds >= 2)
            {
                _lastAttachAttempt = DateTime.UtcNow;
                TryAttach();
            }
        }
    }

    private void Reposition()
    {
        if (!_enabled)
            return;

        // 第一步：先 Show() 创建 HWND（WPF 窗口必须 Show 之后才有句柄）
        if (!IsVisible)
        {
            Show();
            _layoutDirty = true;
            _lastScaleProbe = DateTime.MinValue;
        }

        // 第二步：确认 _handle 已就绪
        if (_handle == IntPtr.Zero)
        {
            _handle = new WindowInteropHelper(this).Handle;
            if (_handle == IntPtr.Zero)
                return;
        }

        // 第三步：SetParent 到 Shell_TrayWnd（Show 之后才能成功）
        EnsureAttached();

        if (!_isAttached || _trayWnd == IntPtr.Zero)
        {
            // 暂时挂不上（比如 Shell_TrayWnd 不存在），先隐藏，等下次重试
            if (IsVisible && _trayWnd == IntPtr.Zero)
                Hide();
            return;
        }

        // 每帧重复测量代价高，只在文本或字号变化后重新测量一次
        if (_layoutDirty)
        {
            UpdateLayout();
            _layoutDirty = false;
        }

        var now = DateTime.UtcNow;
        if ((now - _lastScaleProbe).TotalSeconds >= 2)
        {
            _lastScaleProbe = now;
            var source = PresentationSource.FromVisual(this);
            _scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            _scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            if (_scaleX <= 0) _scaleX = 1;
            if (_scaleY <= 0) _scaleY = 1;
        }

        double scaleX = _scaleX;
        double scaleY = _scaleY;

        double ourWidth = ActualWidth * scaleX;
        double ourHeight = ActualHeight * scaleY;

        // 获取任务栏尺寸
        if (!GetWindowRect(_trayWnd, out var trayRect))
        {
            // 任务栏句柄突然失效（explorer 重启中），重新挂接
            _isAttached = false;
            return;
        }

        int trayWidth = trayRect.Width;
        int trayHeight = trayRect.Height;
        bool horizontal = trayWidth >= trayHeight;

        // 获取通知区锚点
        double anchorPos;
        if (horizontal)
        {
            anchorPos = trayWidth - 6;  // 默认靠最右
            if (_trayNotifyWnd == IntPtr.Zero || !GetWindowRect(_trayNotifyWnd, out _))
                _trayNotifyWnd = FindWindowEx(_trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);

            if (_trayNotifyWnd != IntPtr.Zero && GetWindowRect(_trayNotifyWnd, out var notifyRect))
            {
                // notifyRect 是屏幕坐标，转换成相对 Shell_TrayWnd 的坐标
                int notifyLeft = notifyRect.Left - trayRect.Left;
                if (notifyLeft > 0 && notifyLeft < trayWidth)
                    anchorPos = notifyLeft - 6;
            }
        }
        else
        {
            anchorPos = trayHeight - 6;
            if (_trayNotifyWnd == IntPtr.Zero || !GetWindowRect(_trayNotifyWnd, out _))
                _trayNotifyWnd = FindWindowEx(_trayWnd, IntPtr.Zero, "TrayNotifyWnd", null);

            if (_trayNotifyWnd != IntPtr.Zero && GetWindowRect(_trayNotifyWnd, out var notifyRect))
            {
                int notifyTop = notifyRect.Top - trayRect.Top;
                if (notifyTop > 0 && notifyTop < trayHeight)
                    anchorPos = notifyTop - 6;
            }
        }

        // 相对 Shell_TrayWnd 客户区坐标
        double x, y;
        if (horizontal)
        {
            x = anchorPos - ourWidth;
            y = (trayHeight - ourHeight) / 2.0;
        }
        else
        {
            x = (trayWidth - ourWidth) / 2.0;
            y = anchorPos - ourHeight;
        }

        // 采样任务栏明暗（用屏幕坐标）
        int screenX = trayRect.Left + (int)x;
        int screenY = trayRect.Top + (int)y;
        SampleTaskbarBrightness(screenX, screenY, (int)ourWidth, (int)ourHeight);

        // WPF 的 Left/Top 是宿主坐标系。作为 WS_CHILD 窗口后，设成相对父窗口的坐标就行
        double left = x / scaleX;
        double top = y / scaleY;

        // 位置没变就别重复设置
        bool moved = double.IsNaN(_lastLeft) || double.IsNaN(_lastTop)
            || Math.Abs(left - _lastLeft) >= 0.5 || Math.Abs(top - _lastTop) >= 0.5;

        if (moved)
        {
            _lastLeft = left;
            _lastTop = top;
            Left = left;
            Top = top;
        }
    }

    // ===== P/Invoke =====

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }
}
