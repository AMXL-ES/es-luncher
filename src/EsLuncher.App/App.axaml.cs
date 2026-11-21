using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using EsLuncher.Core.Settings;

namespace EsLuncher.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        StartupTrace.Mark("framework init");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RequestedThemeVariant = AppServices.Settings.Theme == LauncherSettings.ThemeLight
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            Localization.Language = AppServices.Settings.Language;
            desktop.MainWindow = new MainWindow();
            StartupTrace.Mark("main window constructed");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
