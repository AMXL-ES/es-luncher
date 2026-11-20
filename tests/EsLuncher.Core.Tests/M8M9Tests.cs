using System.IO.Compression;
using DomainContent = EsLuncher.Core.Content;
using EsLuncher.Core.Diagnostics;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public class ModrinthTests
{
    [Fact]
    public void Builds_search_uri_with_facets()
    {
        var uri = DomainContent.ModrinthClient.BuildSearchUri("jei", "1.18.2", "fabric", 10);

        Assert.Contains("query=jei", uri.Query);
        Assert.Contains("limit=10", uri.Query);
        Assert.Contains("facets=", uri.Query);
        var facets = Uri.UnescapeDataString(uri.Query);
        Assert.Contains("versions:1.18.2", facets);
        Assert.Contains("categories:fabric", facets);
    }

    [Fact]
    public void Parses_search_hits()
    {
        const string json = """
        {"hits":[{"project_id":"P1","slug":"jei","title":"Just Enough Items","description":"recipes","downloads":123456}]}
        """;

        var hits = DomainContent.ModrinthClient.ParseSearch(json);

        Assert.Single(hits);
        Assert.Equal("jei", hits[0].Slug);
        Assert.Equal(123456, hits[0].Downloads);
    }

    [Fact]
    public void Parses_version_dependencies()
    {
        const string json = """
        [{"id":"V1","version_number":"1.0","files":[],
          "dependencies":[{"project_id":"P9","dependency_type":"required"},
                          {"project_id":"P8","dependency_type":"optional"}]}]
        """;

        var versions = DomainContent.ModrinthClient.ParseVersions(json);

        Assert.Equal(2, versions[0].Dependencies.Count);
        Assert.Contains(versions[0].Dependencies, dependency => dependency.DependencyType == "required");
    }

    [Fact]
    public void Parses_versions_and_primary_file()
    {
        const string json = """
        [{"id":"V1","version_number":"1.2.3","files":[
          {"url":"https://cdn.test/jei.jar","filename":"jei.jar","primary":true,"size":10,
           "hashes":{"sha1":"abc"}}],"game_versions":["1.18.2"]}]
        """;

        var versions = DomainContent.ModrinthClient.ParseVersions(json);

        Assert.Single(versions);
        Assert.Equal("1.2.3", versions[0].VersionNumber);
        Assert.True(versions[0].Files[0].Primary);
        Assert.Equal("jei.jar", versions[0].Files[0].Filename);
        Assert.Contains("1.18.2", versions[0].GameVersions!);
    }

    [Fact]
    public void Search_uri_supports_index_and_category_facets()
    {
        var uri = DomainContent.ModrinthClient.BuildSearchUri(
            string.Empty, "1.18.2", "fabric", 30, DomainContent.ContentKind.Mods, null, "downloads", ["optimization"]);

        Assert.Contains("index=downloads", uri.Query);
        var facets = Uri.UnescapeDataString(uri.Query);
        Assert.Contains("categories:optimization", facets);
    }

    [Fact]
    public void Parses_project_details()
    {
        const string json = """
        {"id":"P1","slug":"jei","title":"Just Enough Items","description":"recipes",
         "body":"# JEI\nView items","icon_url":"https://cdn.test/jei.png","downloads":123,
         "categories":["storage","utility"]}
        """;

        var project = DomainContent.ModrinthClient.ParseProject(json);

        Assert.NotNull(project);
        Assert.Equal("jei", project!.Slug);
        Assert.Equal("https://cdn.test/jei.png", project.IconUrl);
        Assert.Contains("storage", project.Categories);
    }
}

public class InstanceVersionInfoTests
{
    [Theory]
    [InlineData("fabric-loader-0.20.1-1.18.2", "1.18.2", "fabric-loader")]
    [InlineData("quilt-loader-0.20.0-beta.9-1.18.2", "1.18.2", "quilt-loader")]
    [InlineData("1.18.2-forge-40.3.0", "1.18.2", "forge")]
    [InlineData("1.20.1-neoforge-47.1.106", "1.20.1", "neoforge")]
    [InlineData("1.18.2", "1.18.2", null)]
    public void Derives_mc_and_loader(string versionId, string expectedMc, string? expectedLoader)
    {
        var (mc, loader) = DomainContent.InstanceVersionInfo.Derive(versionId);

        Assert.Equal(expectedMc, mc);
        Assert.Equal(expectedLoader, loader);
    }
}

public sealed class InstancePackTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly DomainContent.InstanceContentService _content;
    private readonly DomainContent.InstancePackService _packs;

    public InstancePackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-pack-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _content = new DomainContent.InstanceContentService(_layout, _instances);
        _packs = new DomainContent.InstancePackService(_layout, _instances, _content);
        _instances.Create("src", "fabric-loader-0.20.1-1.18.2");
        _instances.Create("dst", "fabric-loader-0.20.1-1.18.2");
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
    public async Task Export_then_import_copies_overrides()
    {
        var mod = Path.Combine(_root, "mod.jar");
        File.WriteAllText(mod, "mod-bytes");
        _content.Add("src", DomainContent.ContentKind.Mods, mod);

        var zip = Path.Combine(_root, "pack.mrpack");
        await _packs.ExportAsync("src", zip, CancellationToken.None);

        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.NotNull(archive.GetEntry("modrinth.index.json"));
            Assert.NotNull(archive.GetEntry("overrides/mods/mod.jar"));
        }

        var client = new FakeRemoteFileClient();
        var (downloaded, overridden, skipped) = await _packs.ImportAsync(zip, "dst", client, CancellationToken.None);

        Assert.Equal(0, downloaded);
        Assert.Equal(1, overridden);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(Path.Combine(_layout.GameDir("dst"), "mods", "mod.jar")));
    }

    [Fact]
    public async Task Import_rejects_path_traversal()
    {
        var zip = Path.Combine(_root, "evil.mrpack");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modrinth.index.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{"formatVersion":1,"files":[{"path":"../escape.jar","downloads":["https://x/y"]}]}""");
        }

        var client = new FakeRemoteFileClient();
        await Assert.ThrowsAsync<InvalidDataException>(() => _packs.ImportAsync(zip, "dst", client, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "escape.jar")));
    }

    [Fact]
    public void ResolveTarget_blocks_escape()
    {
        Assert.Throws<InvalidDataException>(() => DomainContent.InstancePackService.ResolveTarget(_root, "../x"));
    }
}

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly InstanceDiagnostics _diagnostics;

    public DiagnosticsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-diag-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _diagnostics = new InstanceDiagnostics(_layout, _instances);
        _instances.Create("dev", "1.18.2");

        var game = _layout.GameDir("dev");
        Directory.CreateDirectory(Path.Combine(game, "logs"));
        File.WriteAllText(
            Path.Combine(game, "logs", "latest.log"),
            """
            [1] [main/INFO]: starting
            [2] [main/ERROR]: java.lang.UnsupportedClassVersionError: net/minecraft/client/main/Main has been compiled by a more recent version
            [3] [main/ERROR]: java.lang.OutOfMemoryError: Java heap space
            """);
        Directory.CreateDirectory(Path.Combine(game, "crash-reports"));
        File.WriteAllText(Path.Combine(game, "crash-reports", "crash-2026.txt"), "Minecraft Crash Report\nDescription: boom");
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
    public void Analyze_finds_errors_hints_and_crashes()
    {
        var report = _diagnostics.Analyze("dev");

        Assert.True(report.Errors.Count >= 2);
        Assert.Single(report.Crashes);
        Assert.Contains(report.Hints, hint => hint.Contains("Java 版本不匹配", StringComparison.Ordinal));
        Assert.Contains(report.Hints, hint => hint.Contains("内存不足", StringComparison.Ordinal));
    }

    [Fact]
    public void Export_writes_diagnostic_bundle()
    {
        var zip = Path.Combine(_root, "diag.zip");

        _diagnostics.Export("dev", zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("logs/latest.log"));
        Assert.NotNull(archive.GetEntry("crash-reports/crash-2026.txt"));
        Assert.NotNull(archive.GetEntry("instance/config.json"));
        Assert.NotNull(archive.GetEntry("system-info.txt"));
    }

    [Fact]
    public void Unknown_instance_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => _diagnostics.Analyze("nobody"));
    }
}
