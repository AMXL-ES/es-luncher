using System.IO.Compression;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public sealed class InstanceStoreTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _store;

    public InstanceStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-instance-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _store = new InstanceStore(_layout);
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
    public void Create_list_and_load_roundtrip()
    {
        var created = _store.Create("整合包 1.18", "1.18.2", memoryMb: 4096, username: "Dev");

        Assert.Equal("整合包 1.18", created.Name);
        Assert.Equal("1.18.2", created.VersionId);
        Assert.Equal(4096, created.MemoryMb);
        Assert.True(File.Exists(_layout.InstanceConfigFile("整合包 1.18")));

        var list = _store.List();
        Assert.Single(list);
        Assert.Equal("整合包 1.18", list[0].Name);

        var loaded = _store.TryLoad("整合包 1.18");
        Assert.NotNull(loaded);
        Assert.Equal(4096, loaded!.MemoryMb);
        Assert.Equal("Dev", loaded.Username);
    }

    [Fact]
    public void Duplicate_instance_is_rejected()
    {
        _store.Create("dev", "1.18.2");

        Assert.Throws<InvalidOperationException>(() => _store.Create("dev", "1.18.2"));
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("")]
    [InlineData(" leading")]
    [InlineData("..")]
    public void Invalid_names_are_rejected(string name)
    {
        Assert.Throws<ArgumentException>(() => _store.Create(name, "1.18.2"));
    }

    [Fact]
    public void Save_updates_configuration()
    {
        var created = _store.Create("dev", "1.18.2");
        _store.Save(created with { MemoryMb = 8192, WindowWidth = 1280, WindowHeight = 720 });

        var loaded = _store.TryLoad("dev");
        Assert.NotNull(loaded);
        Assert.Equal(8192, loaded!.MemoryMb);
        Assert.Equal(1280, loaded.WindowWidth);
    }

    [Fact]
    public void Clone_copies_game_directory_and_renames_config()
    {
        _store.Create("source", "1.18.2");
        var gameDirectory = _layout.GameDir("source");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "level.dat"), "world");

        var cloned = _store.Clone("source", "target");

        Assert.Equal("target", cloned.Name);
        Assert.Equal("1.18.2", cloned.VersionId);
        Assert.Equal("world", File.ReadAllText(Path.Combine(_layout.GameDir("target"), "level.dat")));
        Assert.Equal(2, _store.List().Count);
    }

    [Fact]
    public void Delete_requires_force_then_removes_directory()
    {
        _store.Create("dev", "1.18.2");

        Assert.Throws<InvalidOperationException>(() => _store.Delete("dev", force: false));
        Assert.True(Directory.Exists(_layout.InstanceDir("dev")));

        Assert.True(_store.Delete("dev", force: true));
        Assert.False(Directory.Exists(_layout.InstanceDir("dev")));
        Assert.Empty(_store.List());
    }

    [Fact]
    public void Export_and_import_roundtrip()
    {
        _store.Create("dev", "1.18.2", memoryMb: 3072);
        var gameDirectory = _layout.GameDir("dev");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "level.dat"), "world");

        var zip = Path.Combine(_root, "export.zip");
        _store.Export("dev", zip);
        Assert.True(File.Exists(zip));

        var imported = _store.Import(zip, "导入 实例");

        Assert.Equal("导入 实例", imported.Name);
        Assert.Equal("1.18.2", imported.VersionId);
        Assert.Equal(3072, imported.MemoryMb);
        Assert.Equal("world", File.ReadAllText(Path.Combine(_layout.GameDir("导入 实例"), "level.dat")));
    }

    [Fact]
    public void Import_rejects_zip_slip_and_cleans_up()
    {
        var zip = Path.Combine(_root, "evil.zip");
        Directory.CreateDirectory(_root);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("pwned");
        }

        Assert.Throws<InvalidDataException>(() => _store.Import(zip, "evil"));
        Assert.False(Directory.Exists(_layout.InstanceDir("evil")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void Import_without_config_is_rejected()
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, "empty.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("game/readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("no config here");
        }

        Assert.Throws<InvalidDataException>(() => _store.Import(zip, "no-config"));
        Assert.False(Directory.Exists(_layout.InstanceDir("no-config")));
    }
}
