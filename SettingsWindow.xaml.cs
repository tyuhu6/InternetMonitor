using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace InternetMonitor;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly AppState _state;
    private bool _loading;
    private Action<Color>? _colorApply;

    public SettingsWindow(MainWindow owner)
    {
        InitializeComponent();

        _owner = owner;
        _state = owner.State;

        LoadFonts();
        SyncFromState();
        Topmost = _state.Topmost;

        Picker.ColorChanged += OnPickerColorChanged;
        ColorPopup.Closed += (_, _) =>
        {
            if (_colorApply == null)
                return;

            _colorApply = null;
            _owner.SaveStateNow();
        };
    }

    public void SyncFromState()
    {
        _loading = true;

        MainWindowSwitch.IsChecked = _state.ShowMainWindow;
        TaskbarSwitch.IsChecked = _state.ShowTaskbarSpeed;
        TopmostSwitch.IsChecked = _state.Topmost;
        StartupSwitch.IsChecked = StartupManager.IsEnabled();
        Topmost = _state.Topmost;

        var match = FontCombo.Items.Cast<string>()
            .FirstOrDefault(n => string.Equals(n, _state.FontFamily, StringComparison.OrdinalIgnoreCase));
        FontCombo.SelectedItem = match ?? _state.FontFamily;

        FontScaleText.Text = $"{Math.Round(_state.FontScale * 100)}%";
        SpeedUnitCombo.SelectedIndex = (int)SpeedFormatter.Parse(_state.SpeedUnit);

        UpdateSwatch(BgSwatch, _state.BgColor);
        UpdateSwatch(TextSwatch, _state.TextColor);
        UpdateSwatch(DownSwatch, _state.DownColor);
        UpdateSwatch(UpSwatch, _state.UpColor);

        _loading = false;
    }

    private void LoadFonts()
    {
        var names = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!names.Any(n => string.Equals(n, _state.FontFamily, StringComparison.OrdinalIgnoreCase)))
            names.Insert(0, _state.FontFamily);

        FontCombo.ItemsSource = names;
    }

    private void MainWindowSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _owner.SetMainWindowVisible(MainWindowSwitch.IsChecked == true);
    }

    private void TaskbarSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _owner.SetTaskbarVisible(TaskbarSwitch.IsChecked == true);
    }

    private void TopmostSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _owner.SetTopmost(TopmostSwitch.IsChecked == true);
    }

    private void StartupSwitch_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        bool enabled = StartupSwitch.IsChecked == true;

        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _loading = true;
            StartupSwitch.IsChecked = !enabled;
            _loading = false;

            MessageBox.Show("无法修改开机启动项：" + ex.Message, "网络监控",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontCombo.SelectedItem is not string name)
            return;

        _state.FontFamily = name;
        ApplyAll();
    }

    /// <summary>弹层里的条目在按下时就完成选中，避免弹层收起把点击吞掉。</summary>
    private void ComboItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ComboBoxItem item)
            return;

        if (ItemsControl.ItemsControlFromItemContainer(item) is not ComboBox combo)
            return;

        combo.SelectedIndex = combo.ItemContainerGenerator.IndexFromContainer(item);
        combo.IsDropDownOpen = false;
        e.Handled = true;
    }

    private void SpeedUnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SpeedUnitCombo.SelectedIndex < 0)
            return;

        _state.SpeedUnit = ((SpeedUnitMode)SpeedUnitCombo.SelectedIndex).ToString();
        _owner.ApplySpeedUnit();
        _owner.SaveStateNow();
    }

    private void FontMinus_Click(object sender, RoutedEventArgs e) => ChangeScale(-0.05);
    private void FontPlus_Click(object sender, RoutedEventArgs e) => ChangeScale(0.05);

    private void ChangeScale(double delta)
    {
        _state.FontScale = Math.Round(Math.Clamp(_state.FontScale + delta, 0.8, 1.5), 2);
        FontScaleText.Text = $"{Math.Round(_state.FontScale * 100)}%";
        ApplyAll();
    }

    private void BgColor_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(BgColorButton, _state.BgColor, color =>
        {
            _state.BgColor = Appearance.ToHex(color);
            UpdateSwatch(BgSwatch, _state.BgColor);
        });
    }

    private void TextColor_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(TextColorButton, _state.TextColor, color =>
        {
            _state.TextColor = Appearance.ToHex(color);
            UpdateSwatch(TextSwatch, _state.TextColor);
        });
    }

    private void DownColor_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(DownColorButton, _state.DownColor, color =>
        {
            _state.DownColor = Appearance.ToHex(color);
            UpdateSwatch(DownSwatch, _state.DownColor);
        });
    }

    private void UpColor_Click(object sender, RoutedEventArgs e)
    {
        OpenColorPicker(UpColorButton, _state.UpColor, color =>
        {
            _state.UpColor = Appearance.ToHex(color);
            UpdateSwatch(UpSwatch, _state.UpColor);
        });
    }

    private void OpenColorPicker(Button anchor, string current, Action<Color> apply)
    {
        _colorApply = apply;
        Picker.SetColor(Appearance.ParseHex(current, Colors.White));
        ColorPopup.PlacementTarget = anchor;
        ColorPopup.IsOpen = true;
    }

    private void OnPickerColorChanged(Color color)
    {
        _colorApply?.Invoke(color);
        _owner.ApplyAppearance();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _state.FontFamily = "Microsoft YaHei UI";
        _state.FontScale = 1.0;
        _state.SpeedUnit = nameof(SpeedUnitMode.Auto);
        _state.BgColor = "#FFFFFF";
        _state.TextColor = "#101318";
        _state.DownColor = "#2F6BFF";
        _state.UpColor = "#E8890C";

        SyncFromState();
        ApplyAll();
    }

    private void ApplyAll()
    {
        _owner.ApplyAppearance();
        _owner.SaveStateNow();
    }

    private static void UpdateSwatch(Border swatch, string hex)
    {
        swatch.Background = new SolidColorBrush(Appearance.ParseHex(hex, Colors.Transparent));
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        // 点到开关、下拉、色块等控件时交给控件自己处理
        if (IsInteractive(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标提前松开等情况下会抛异常，忽略即可
        }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase || source is ComboBox || source is TextBox || source is ScrollBar)
                return true;

            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }

        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
