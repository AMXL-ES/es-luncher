using System.IO.Compression;
using GLauncher.Core.Downloads;

namespace GLauncher.Core.Tests;

public sealed class NativesExtractorTests : IDisposable
{
    private readonly string _root;

    public NativesExtractorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "glauncher-natives-tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Extracts_entries_and_skips_excluded_prefixes()
    {
        var jar = CreateZip("natives.jar", new()
        {
            ["META-INF/MANIFEST.MF"] = "manifest",
            ["lwjgl.dll"] = "binary",
            ["sub/dir/opengl32.dll"] = "binary2",
        });
        var target = Path.Combine(_root, "out");

        var extracted = NativesExtractor.Extract(jar, target, ["META-INF/"]);

        Assert.Equal(2, extracted);
        Assert.Equal("binary", File.ReadAllText(Path.Combine(target, "lwjgl.dll")));
        Assert.Equal("binary2", File.ReadAllText(Path.Combine(target, "sub", "dir", "opengl32.dll")));
        Assert.False(File.Exists(Path.Combine(target, "META-INF", "MANIFEST.MF")));
    }

    [Fact]
    public void Rejects_zip_slip_entries()
    {
        var jar = CreateZip("evil.jar", new()
        {
            ["../escaped.txt"] = "pwned",
        });
        var target = Path.Combine(_root, "safe");

        var exception = Assert.Throws<InvalidDataException>(() => NativesExtractor.Extract(jar, target, []));

        Assert.Contains("zip slip", exception.Message);
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    private string CreateZip(string name, Dictionary<string, string> entries)
    {
        var path = Path.Combine(_root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        return path;
    }
}
