using System.Text.Json;

namespace EsLuncher.Core.Settings;

/// <summary>启动器级设置（与实例无关），JSON 落盘在数据根。</summary>
public sealed record LauncherSettings
{
    public const string ThemeDark = "dark";
    public const string ThemeLight = "light";

    public string Theme { get; init; } = ThemeDark;

    public int DefaultMemoryMb { get; init; } = 2048;

    public string? JavaPath { get; init; }

    public string Language { get; init; } = "zh";

    /// <summary>默认用户名（新建实例时使用，空 = Player）。</summary>
    public string? DefaultUsername { get; init; }

    /// <summary>下载并发数（1-16）。</summary>
    public int Concurrency { get; init; } = 8;

    /// <summary>缺少 Java 时是否自动下载官方运行时。</summary>
    public bool AutoInstallJava { get; init; }
}

public sealed class SettingsStore(Downloads.DataLayout layout)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath => Path.Combine(layout.Root, "launcher-settings.json");

    public LauncherSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new LauncherSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath), JsonOptions)
                ?? new LauncherSettings();
        }
        catch (JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        Directory.CreateDirectory(layout.Root);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
