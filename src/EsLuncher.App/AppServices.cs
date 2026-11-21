using EsLuncher.Core.Downloads;
using EsLuncher.Core.Settings;
using EsLuncher.Core.Updates;

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

    /// <summary>
    /// 自动更新服务。更新源可用 <c>ESLUNCHER_UPDATE_SOURCE</c> 覆盖（本地目录或 URL），
    /// 不设则用 Velopack 安装信息里的默认源。日志固定落 <c>数据根/logs/update.log</c>。
    /// </summary>
    public static IUpdateService CreateUpdateService()
    {
        var source = Environment.GetEnvironmentVariable("ESLUNCHER_UPDATE_SOURCE");
        var logPath = Path.Combine(Layout.Root, "logs", "update.log");
        return new VelopackUpdateService(
            string.IsNullOrWhiteSpace(source) ? null : source,
            message =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
                }
                catch (IOException)
                {
                    // 日志失败不影响更新流程。
                }
            });
    }

    public static void SaveSettings(LauncherSettings settings)
    {
        Settings = settings;
        SettingsStore.Save(settings);
        SettingsChanged?.Invoke();
    }

    private static DataLayout CreateLayout()
    {
        return new DataLayout(DataLayout.DefaultRoot());
    }
}
