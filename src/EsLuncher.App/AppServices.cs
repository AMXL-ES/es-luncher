using EsLuncher.Core.Downloads;
using EsLuncher.Core.Settings;

namespace EsLuncher.App;

/// <summary>UI 进程级的共享服务（数据根、设置）。业务逻辑仍全部在 Core。</summary>
public static class AppServices
{
    public static DataLayout Layout { get; } = CreateLayout();

    public static SettingsStore SettingsStore { get; } = new(Layout);

    public static LauncherSettings Settings { get; private set; } = SettingsStore.Load();

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
