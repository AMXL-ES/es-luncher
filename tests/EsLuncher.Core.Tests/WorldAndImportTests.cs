using System.IO.Compression;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public sealed class WorldManagerTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly WorldManager _worlds;

    public WorldManagerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-world-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _worlds = new WorldManager(_layout, _instances);
        _instances.Create("dev", "1.18.2");
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

    private string CreateWorld(string name, int bytes = 10)
    {
        var path = Path.Combine(_layout.GameDir("dev"), "saves", name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "level.dat"), new string('x', bytes));
        return path;
    }

    [Fact]
    public void Lists_worlds_with_size()
    {
        CreateWorld("Alpha", 2048);

        var worlds = _worlds.List("dev");

        Assert.Single(worlds);
        Assert.Equal("Alpha", worlds[0].Name);
        Assert.True(worlds[0].SizeBytes >= 2048);
    }

    [Fact]
    public void Backup_creates_zip_and_restore_roundtrips()
    {
        CreateWorld("Alpha", 32);
        var zip = Path.Combine(_root, "alpha.zip");

        _worlds.Backup("dev", "Alpha", zip);
        Assert.True(File.Exists(zip));

        var restored = _worlds.Restore("dev", zip, "Alpha Copy");
        Assert.True(File.Exists(Path.Combine(restored, "level.dat")));
        Assert.Equal(2, _worlds.List("dev").Count);
    }

    [Fact]
    public void Restore_refuses_existing_world()
    {
        CreateWorld("Alpha", 32);
        var zip = Path.Combine(_root, "alpha.zip");
        _worlds.Backup("dev", "Alpha", zip);

        Assert.Throws<InvalidOperationException>(() => _worlds.Restore("dev", zip, "Alpha"));
    }

    [Fact]
    public void Rename_moves_world()
    {
        CreateWorld("Alpha", 8);

        _worlds.Rename("dev", "Alpha", "Beta");

        Assert.Single(_worlds.List("dev"));
        Assert.Equal("Beta", _worlds.List("dev")[0].Name);
    }

    [Fact]
    public void Rename_rejects_existing_target()
    {
        CreateWorld("Alpha", 8);
        CreateWorld("Beta", 8);

        Assert.Throws<InvalidOperationException>(() => _worlds.Rename("dev", "Alpha", "Beta"));
    }
}

public sealed class InstanceImporterTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly InstanceImporter _importer;

    public InstanceImporterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-import-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _importer = new InstanceImporter(_layout, _instances);
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
    public void Imports_content_without_touching_source()
    {
        var source = Path.Combine(_root, "minecraft");
        Directory.CreateDirectory(Path.Combine(source, "mods"));
        Directory.CreateDirectory(Path.Combine(source, "saves", "w1"));
        File.WriteAllText(Path.Combine(source, "mods", "a.jar"), "jar");
        File.WriteAllText(Path.Combine(source, "options.txt"), "fov:90");

        var instance = _importer.Import("imported", source, "1.18.2");

        Assert.Equal("1.18.2", instance.VersionId);
        Assert.True(File.Exists(Path.Combine(_layout.GameDir("imported"), "mods", "a.jar")));
        Assert.True(File.Exists(Path.Combine(_layout.GameDir("imported"), "options.txt")));
        Assert.True(Directory.Exists(Path.Combine(_layout.GameDir("imported"), "saves", "w1")));
        Assert.True(File.Exists(Path.Combine(source, "mods", "a.jar"))); // 源不动
    }
}

public sealed class ModStatusTests
{
    [Theory]
    [InlineData("10.2.1.1002", "10.2.1.1002", false)]
    [InlineData("1.18.2-10.2.1.1002", "10.2.1.1002", false)]
    [InlineData("1.0.0", "1.1.0", true)]
    public void Compares_versions_heuristically(string local, string latest, bool expected)
    {
        var status = new ModStatus("mod.jar", "mod", local, latest, "mod");

        Assert.Equal(expected, status.UpdateAvailable);
    }
}
