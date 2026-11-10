using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using ComboBox = System.Windows.Controls.ComboBox;

namespace GarlandForWindows;

internal sealed class SettingsWindow : Window
{
    private readonly CheckBox taskbar = new() { Content = "Панель задач", Margin = new Thickness(0,4,0,4) };
    private readonly CheckBox frame = new() { Content = "Рамка активного окна", Margin = new Thickness(0,4,0,4) };
    private readonly CheckBox startup = new() { Content = "Запускать при входе в Windows", Margin = new Thickness(0,8,0,4) };
    private readonly ComboBox theme = new() { Margin = new Thickness(0,4,0,8) };
    private readonly Slider brightness = new() { Minimum = 10, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly Slider speed = new() { Minimum = 25, Maximum = 200, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,4) };
    private readonly Func<Settings, bool, bool?, string?> apply;
    private Settings current;
    private bool reset, startupInitial, startupAvailable, moved;
    private static readonly string[] Names = ["Классическая", "Тёплый белый", "Холодный белый", "Разноцветная"];
    internal SettingsWindow(Settings settings, bool future, Func<Settings, bool, bool?, string?> apply)
    {
        this.current = settings; this.apply = apply; Title = "Настройки гирлянды"; Width = 420; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        var panel = new StackPanel { Margin = new Thickness(20) }; Content = panel;
        panel.Children.Add(taskbar); panel.Children.Add(frame); panel.Children.Add(new TextBlock { Text = "Тема" });
        foreach (string name in Names) theme.Items.Add(name); panel.Children.Add(theme);
        var brightnessLabel = new TextBlock(); brightness.ValueChanged += (_, _) => brightnessLabel.Text = $"Яркость: {brightness.Value:0}%"; panel.Children.Add(brightnessLabel); panel.Children.Add(brightness);
        var speedLabel = new TextBlock { Margin = new Thickness(0,8,0,0) }; speed.ValueChanged += (_, _) => speedLabel.Text = $"Скорость: {speed.Value:0}%"; panel.Children.Add(speedLabel); panel.Children.Add(speed);
        var state = AutoStartService.Read(); startupInitial = state.Enabled; startupAvailable = state.Error == null; moved = state.Moved;
        startup.IsChecked = state.Enabled; startup.IsEnabled = startupAvailable; panel.Children.Add(startup);
        if (state.Error != null) status.Text = "Автозапуск недоступен: " + state.Error;
        else if (moved) status.Text = "Путь автозапуска отличается. Нажмите «Применить», чтобы обновить его.";
        if (future) status.Text += "\nФайл настроек создан более новой версией. Для сброса выберите «По умолчанию» и примените.";
        panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
        Button defaults = new() { Content = "По умолчанию", Padding = new Thickness(8,4,8,4), Margin = new Thickness(0,0,8,0) };
        Button ok = new() { Content = "Применить", Padding = new Thickness(8,4,8,4), Margin = new Thickness(0,0,8,0) };
        Button cancel = new() { Content = "Отмена", Padding = new Thickness(8,4,8,4) };
        buttons.Children.Add(defaults); buttons.Children.Add(ok); buttons.Children.Add(cancel); panel.Children.Add(buttons);
        defaults.Click += (_, _) => { Load(new Settings()); startup.IsChecked = false; reset = true; };
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) => {
            var draft = current with { TaskbarEnabled = taskbar.IsChecked == true, WindowFrameEnabled = frame.IsChecked == true, Theme = Settings.Themes[Math.Max(0, theme.SelectedIndex)], BrightnessPercent = (int)brightness.Value, SpeedPercent = (int)speed.Value };
            bool desired = startup.IsChecked == true;
            bool? change = startupAvailable && (desired != startupInitial || (desired && moved)) ? desired : null;
            string? error = apply(draft, reset, change);
            if (error != null) { status.Text = error; var now = AutoStartService.Read(); if (now.Error == null) { startupInitial = now.Enabled; moved = now.Moved; } return; }
            current = draft; reset = false; startupInitial = desired; moved = false; status.Text = "Настройки применены.";
        };
        Load(settings);
    }
    private void Load(Settings s) { taskbar.IsChecked = s.TaskbarEnabled; frame.IsChecked = s.WindowFrameEnabled; theme.SelectedIndex = Math.Max(0, Array.IndexOf(Settings.Themes, s.Theme)); brightness.Value = s.BrightnessPercent; speed.Value = s.SpeedPercent; }
    internal void SyncTray(Settings s) { current = current with { GarlandsEnabled = s.GarlandsEnabled, TaskbarEnabled = s.TaskbarEnabled, WindowFrameEnabled = s.WindowFrameEnabled }; taskbar.IsChecked = s.TaskbarEnabled; frame.IsChecked = s.WindowFrameEnabled; }
}
