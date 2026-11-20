using EsLuncher.Core.Downloads;
using EsLuncher.Core.Settings;

namespace EsLuncher.Core.Tests;

public sealed class LauncherSettingsTests : IDisposable
{
    private readonly string _root;
    private readonly SettingsStore _store;

    public LauncherSettingsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-settings-tests", Guid.NewGuid().ToString("N"));
        _store = new SettingsStore(new DataLayout(_root));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var settings = _store.Load();

        Assert.Equal(LauncherSettings.ThemeDark, settings.Theme);
        Assert.Equal(2048, settings.DefaultMemoryMb);
        Assert.Null(settings.JavaPath);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        _store.Save(new LauncherSettings
        {
            Theme = LauncherSettings.ThemeLight,
            DefaultMemoryMb = 6144,
            JavaPath = @"D:\java\bin\java.exe",
            Language = "zh",
        });

        var loaded = _store.Load();

        Assert.Equal(LauncherSettings.ThemeLight, loaded.Theme);
        Assert.Equal(6144, loaded.DefaultMemoryMb);
        Assert.Equal(@"D:\java\bin\java.exe", loaded.JavaPath);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "launcher-settings.json"), "{ not json");

        Assert.Equal(LauncherSettings.ThemeDark, _store.Load().Theme);
    }
}
