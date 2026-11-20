using EsLuncher.Core.Downloads;
using EsLuncher.Core.Settings;

namespace EsLuncher.App;

/// <summary>UI 进程级的共享服务（数据根、设置）。业务逻辑仍全部在 Core。</summary>
public static class AppServices
{
    public static DataLayout Layout { get; } = CreateLayout();

    public static SettingsStore SettingsStore { get; } = new(Layout);

    public static LauncherSettings Settings { get; private set; } = ApplyOverrides(SettingsStore.Load());

    private static LauncherSettings ApplyOverrides(LauncherSettings settings)
    {
        var language = Environment.GetEnvironmentVariable("ESLUNCHER_LANGUAGE");
        var source = Environment.GetEnvironmentVariable("ESLUNCHER_DOWNLOAD_SOURCE") ?? settings.DownloadSource;
        DownloadSourcePolicy.Source = string.Equals(source, "bmclapi", StringComparison.OrdinalIgnoreCase)
            ? DownloadSource.Bmclapi
            : DownloadSource.Official;
        return string.IsNullOrWhiteSpace(language) ? settings : settings with { Language = language };
    }

    public static event Action? SettingsChanged;

    public static void SaveSettings(LauncherSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        SettingsChanged?.Invoke();
    }

    private static DataLayout CreateLayout()
    {
        var root = Environment.GetEnvironmentVariable("ESLUNCHER_DATA");
        return new DataLayout(string.IsNullOrWhiteSpace(root) ? @"D:\mc-data" : root!);
    }
}
