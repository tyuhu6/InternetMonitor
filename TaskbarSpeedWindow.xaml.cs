using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace InternetMonitor;

/// <summary>贴在任务栏通知区域左侧的极简网速读数，鼠标可穿透。</summary>
public partial class TaskbarSpeedWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // 16ms 贴近 60 帧，任务栏滑动时逐帧跟随；空闲时只有几次 GetWindowRect，不占 CPU
    private readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private Appearance _appearance = new();
    private bool _taskbarIsDark = true;
    private bool _enabled;
    private bool _layoutDirty;
    private double _scaleX = 1;
    private double _scaleY = 1;
    private double _lastLeft = double.NaN;
    private double _lastTop = double.NaN;
    private DateTime _lastScaleProbe = DateTime.MinValue;
    private DateTime _lastContrastSample = DateTime.MinValue;

    public TaskbarSpeedWindow()
    {
        InitializeComponent();
        ApplyAppearance(new Appearance());

        _followTimer.Tick += (_, _) => Reposition();
    }

    /// <summary>持续跟随任务栏，滑入滑出时与任务栏同帧移动。</summary>
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
        if (DateTime.UtcNow - _lastContrastSample < TimeSpan.FromSeconds(5))
            return;

        _lastContrastSample = DateTime.UtcNow;

        int sampleX = physicalX - 100;
        if (sampleX < 0)
            sampleX = physicalX + width + 16;

        try
        {
            using var bitmap = new System.Drawing.Bitmap(48, 6);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(sampleX, physicalY + Math.Max(0, height / 2 - 3), 0, 0, bitmap.Size);

            double total = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    total += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                }
            }

            bool dark = total / (bitmap.Width * bitmap.Height) / 255 < 0.5;
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
        DownText.Text = Compact(rxBytesPerSec);
        UpText.Text = Compact(txBytesPerSec);
        _layoutDirty = true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
    }

    private void Reposition()
    {
        if (!_enabled)
            return;

        var layout = TaskbarLayoutDetector.Detect();

        if (layout == null || layout.Hidden)
        {
            if (IsVisible)
                Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
            _layoutDirty = true;
            _lastScaleProbe = DateTime.MinValue;
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

        double width = ActualWidth * scaleX;
        double height = ActualHeight * scaleY;

        double x;
        double y;
        if (layout.Horizontal)
        {
            x = layout.AnchorLeft - width - 6;
            // 以任务栏本体位置为准：自动隐藏时任务栏滑入/滑出，读数跟着一起进出屏幕
            y = layout.Bar.Top + (layout.Bar.Height - height) / 2;
        }
        else
        {
            x = layout.Bar.Left + (layout.Bar.Width - width) / 2;
            y = layout.AnchorTop - height - 6;
        }

        if (IsInsideMonitor(layout, x, y, width, height))
            SampleTaskbarBrightness((int)x, (int)y, (int)width, (int)height);

        double left = x / scaleX;
        double top = y / scaleY;

        // 位置没变就别重复设置窗口位置，省掉多余的 SetWindowPos
        if (Math.Abs(left - _lastLeft) < 0.5 && Math.Abs(top - _lastTop) < 0.5)
            return;

        _lastLeft = left;
        _lastTop = top;
        Left = left;
        Top = top;
    }

    /// <summary>读数完全落在屏幕内时才允许截屏取色，避免截到屏幕外。</summary>
    private static bool IsInsideMonitor(TaskbarLayout layout, double x, double y, double width, double height)
    {
        var monitor = layout.Monitor;
        return x >= monitor.Left && y >= monitor.Top
            && x + width <= monitor.Right && y + height <= monitor.Bottom;
    }

    private static string Compact(double bytesPerSecond)
    {
        double value = Math.Max(0, bytesPerSecond);

        if (value < 1024)
            return value.ToString("0") + "B/s";
        if (value < 1024d * 1024)
            return (value / 1024).ToString("0.#") + "K/s";
        if (value < 1024d * 1024 * 1024)
            return (value / (1024d * 1024)).ToString("0.#") + "M/s";
        return (value / (1024d * 1024 * 1024)).ToString("0.#") + "G/s";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private sealed class TaskbarLayout
    {
        public NativeRect Bar;
        public NativeRect Monitor;
        public bool Horizontal;
        public double AnchorLeft;
        public double AnchorTop;
        public bool Hidden;
    }

    private static class TaskbarLayoutDetector
    {
        private static IntPtr _bar;
        private static IntPtr _tray;
        private static NativeRect _monitor;
        private static DateTime _lastSlowProbe = DateTime.MinValue;
        private static bool _fullscreen;

        public static TaskbarLayout? Detect()
        {
            // 逐帧调用，句柄和显示器范围做缓存，只有任务栏位置本身必须实时读取
            if (_bar == IntPtr.Zero || !GetWindowRect(_bar, out _))
                _bar = FindWindow("Shell_TrayWnd", null);

            IntPtr bar = _bar;
            if (bar == IntPtr.Zero || !GetWindowRect(bar, out var barRect) || barRect.Width <= 0 || barRect.Height <= 0)
                return null;

            var now = DateTime.UtcNow;
            if ((now - _lastSlowProbe).TotalMilliseconds >= 250)
            {
                _lastSlowProbe = now;
                ProbeSlow(bar);
            }

            var layout = new TaskbarLayout
            {
                Bar = barRect,
                Monitor = _monitor,
                Horizontal = barRect.Width >= barRect.Height,
                Hidden = _fullscreen
            };

            if (_tray == IntPtr.Zero || !GetWindowRect(_tray, out _))
                _tray = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);

            var trayRect = default(NativeRect);
            bool hasTray = _tray != IntPtr.Zero && GetWindowRect(_tray, out trayRect) && trayRect.Width > 0;

            if (layout.Horizontal)
                layout.AnchorLeft = hasTray ? trayRect.Left : barRect.Right - 180;
            else
                layout.AnchorTop = hasTray ? trayRect.Top : barRect.Bottom - 180;

            return layout;
        }

        /// <summary>显示器范围与全屏判定变化不频繁，限频探测即可。</summary>
        private static void ProbeSlow(IntPtr bar)
        {
            var monitor = MonitorFromWindow(bar, MONITOR_DEFAULTTONEAREST);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                _monitor = info.Monitor;

            _fullscreen = IsFullscreenForeground(bar);
        }

        private static bool IsFullscreenForeground(IntPtr taskbar)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == taskbar)
                return false;

            var className = new StringBuilder(256);
            GetClassName(foreground, className, className.Capacity);
            string name = className.ToString();

            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
                return false;

            // 最大化窗口的窗口矩形同样覆盖整个屏幕，但它不是全屏应用
            if (IsZoomed(foreground))
                return false;

            if (!GetWindowRect(foreground, out var rect))
                return false;

            var monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info))
                return false;

            return rect.Left <= info.Monitor.Left + 2
                && rect.Top <= info.Monitor.Top + 2
                && rect.Right >= info.Monitor.Right - 2
                && rect.Bottom >= info.Monitor.Bottom - 2;
        }
    }
}
