using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
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
        UsernameBox.Text = settings.DefaultUsername ?? string.Empty;
        ConcurrencyBox.Value = settings.Concurrency;
        AutoJavaBox.IsChecked = settings.AutoInstallJava;
        JavaBox.Text = settings.JavaPath ?? string.Empty;
        ThemeBox.SelectedIndex = settings.Theme == LauncherSettings.ThemeLight ? 1 : 0;
        LanguageBox.SelectedIndex = settings.Language == "en" ? 1 : 0;
        FillJavaList();
    }

    private void FillJavaList()
    {
        try
        {
            var runtimes = JavaDiscovery.Discover([AppServices.Layout.JavaDir]);
            JavaListText.Text = runtimes.Count == 0
                ? "（还没有托管运行时）"
                : string.Join(Environment.NewLine, runtimes.Select(runtime =>
                    $"Java {runtime.MajorVersion} · {runtime.JavaExePath}"));
        }
        catch (Exception exception)
        {
            JavaListText.Text = $"读取失败：{exception.Message}";
        }
    }

    private void OnOpenDataRootClick(object? sender, RoutedEventArgs e)
        => OpenDirectory(AppServices.Layout.Root);

    private void OnOpenTrashClick(object? sender, RoutedEventArgs e)
        => OpenDirectory(Path.Combine(AppServices.Layout.Root, "trash"));

    private void OnOpenJavaDirClick(object? sender, RoutedEventArgs e)
        => OpenDirectory(AppServices.Layout.JavaDir);

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path)
        {
            UseShellExecute = true,
        });
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var theme = ThemeBox.SelectedIndex == 1 ? LauncherSettings.ThemeLight : LauncherSettings.ThemeDark;
        var settings = new LauncherSettings
        {
            Theme = theme,
            DefaultMemoryMb = (int)(MemoryBox.Value ?? 2048),
            DefaultUsername = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text!.Trim(),
            Concurrency = Math.Clamp((int)(ConcurrencyBox.Value ?? 8), 1, 16),
            AutoInstallJava = AutoJavaBox.IsChecked == true,
            JavaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? null : JavaBox.Text!.Trim(),
            Language = LanguageBox.SelectedIndex == 1 ? "en" : "zh",
        };

        AppServices.SaveSettings(settings);
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme == LauncherSettings.ThemeLight ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        StatusText.Text = "已保存（主题立即生效，语言重启后生效）";
    }
}
