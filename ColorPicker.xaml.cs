using System;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InternetMonitor;

/// <summary>自绘取色面板：饱和度/明度方块 + 色相条 + HEX 输入 + 常用预设。</summary>
public partial class ColorPicker : UserControl
{
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private bool _syncing;

    public ColorPicker()
    {
        InitializeComponent();

        SvBox.Picked += (_, _) =>
        {
            if (_syncing)
                return;

            _hue = SvBox.Hue;
            _saturation = SvBox.Saturation;
            _value = SvBox.Value;
            Commit();
        };

        HueBar.HueChanged += (_, _) =>
        {
            if (_syncing)
                return;

            _hue = HueBar.Hue;
            SvBox.Hue = _hue;
            Commit();
        };
    }

    public event Action<Color>? ColorChanged;

    public Color SelectedColor => ColorMath.HsvToColor(_hue, _saturation, _value);

    /// <summary>外部设置颜色，不会触发 ColorChanged。</summary>
    public void SetColor(Color color)
    {
        var (hue, saturation, value) = ColorMath.ColorToHsv(color);
        if (saturation <= 0.001)
            hue = _hue; // 灰阶时保留原色相，避免拖动后跳色

        _hue = hue;
        _saturation = saturation;
        _value = value;
        SyncWidgets();
    }

    private void Commit()
    {
        SyncWidgets();
        ColorChanged?.Invoke(SelectedColor);
    }

    private void SyncWidgets()
    {
        _syncing = true;

        SvBox.Hue = _hue;
        SvBox.Saturation = _saturation;
        SvBox.Value = _value;
        HueBar.Hue = _hue;

        var color = SelectedColor;
        PreviewChip.Background = new SolidColorBrush(color);
        HexBox.Text = Appearance.ToHex(color).ToUpperInvariant();

        _syncing = false;
    }

    private void Preset_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex })
            return;

        SetColor(Appearance.ParseHex(hex, SelectedColor));
        ColorChanged?.Invoke(SelectedColor);
    }

    private void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ApplyHex();
        e.Handled = true;
    }

    private void HexBox_LostFocus(object sender, System.Windows.RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        string text = HexBox.Text.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        if (text.Length == 7 && Appearance.TryParseHex(text, out var color))
        {
            SetColor(color);
            ColorChanged?.Invoke(SelectedColor);
            return;
        }

        HexBox.Text = Appearance.ToHex(SelectedColor).ToUpperInvariant();
    }
}
