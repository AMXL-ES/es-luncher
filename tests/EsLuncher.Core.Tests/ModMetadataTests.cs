using System.IO.Compression;
using EsLuncher.Core.Content;

namespace EsLuncher.Core.Tests;

public sealed class ModMetadataTests : IDisposable
{
    private readonly string _root;

    public ModMetadataTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-modmeta-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    private string CreateJar(string fileName, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(_root, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }

    [Fact]
    public void Reads_fabric_metadata_and_dependencies()
    {
        var jar = CreateJar("fabric-mod.jar", ("fabric.mod.json", """
            {"schemaVersion":1,"id":"example-mod","name":"Example Mod","version":"1.2.3",
             "depends":{"minecraft":">=1.18","fabricloader":">=0.14.0","fabric-api":"*","cloth-config":">=6.0"}}
            """));

        var metadata = ModMetadataReader.Read(jar);

        Assert.Equal("example-mod", metadata.ModId);
        Assert.Equal("Example Mod", metadata.Name);
        Assert.Equal("1.2.3", metadata.Version);
        Assert.Equal("fabric", metadata.Loader);
        Assert.Contains("cloth-config", metadata.Dependencies);
        Assert.DoesNotContain("minecraft", metadata.Dependencies);
        Assert.DoesNotContain("fabric-api", metadata.Dependencies);
    }

    [Fact]
    public void Reads_forge_mods_toml()
    {
        var jar = CreateJar("forge-mod.jar", ("META-INF/mods.toml", """
            modLoader="javafml"
            loaderVersion="[40,)"
            [[mods]]
            modId="forge-example"
            version="2.0.0"
            displayName="Forge Example"
            [[dependencies.forge-example]]
            modId="minecraft"
            [[dependencies.forge-example]]
            modId="jei"
            """));

        var metadata = ModMetadataReader.Read(jar);

        Assert.Equal("forge-example", metadata.ModId);
        Assert.Equal("Forge Example", metadata.Name);
        Assert.Equal("2.0.0", metadata.Version);
        Assert.Equal("forge", metadata.Loader);
    }

    [Fact]
    public void Unknown_jar_returns_unknown()
    {
        var jar = CreateJar("plain.jar", ("a.txt", "nothing"));

        var metadata = ModMetadataReader.Read(jar);

        Assert.Equal("unknown", metadata.Loader);
        Assert.Null(metadata.ModId);
    }

    [Fact]
    public void Broken_jar_does_not_throw()
    {
        var path = Path.Combine(_root, "broken.jar");
        File.WriteAllText(path, "not a zip");

        Assert.Equal("unknown", ModMetadataReader.Read(path).Loader);
    }
}
