using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using EsLuncher.Core.Settings;

namespace EsLuncher.App;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataRootText.Text = AppServices.Layout.Root;
        var settings = AppServices.Settings;
        MemoryBox.Value = settings.DefaultMemoryMb;
        JavaBox.Text = settings.JavaPath ?? string.Empty;
        ThemeBox.SelectedIndex = settings.Theme == LauncherSettings.ThemeLight ? 1 : 0;
    }

    private void OnOpenDataRootClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", AppServices.Layout.Root)
        {
            UseShellExecute = true,
        });

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var theme = ThemeBox.SelectedIndex == 1 ? LauncherSettings.ThemeLight : LauncherSettings.ThemeDark;
        var settings = new LauncherSettings
        {
            Theme = theme,
            DefaultMemoryMb = (int)(MemoryBox.Value ?? 2048),
            JavaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? null : JavaBox.Text!.Trim(),
            Language = AppServices.Settings.Language,
        };

        AppServices.SaveSettings(settings);
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme == LauncherSettings.ThemeLight ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        StatusText.Text = "已保存（主题立即生效）";
    }
}
