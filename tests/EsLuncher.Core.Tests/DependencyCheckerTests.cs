using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public sealed class DependencyCheckerTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly InstanceContentService _content;
    private readonly DependencyChecker _checker;

    public DependencyCheckerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-deps-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _content = new InstanceContentService(_layout, _instances);
        _checker = new DependencyChecker(_content);
        _instances.Create("dev", "fabric-loader-0.20.1-1.18.2");
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

    private void AddMod(string fileName, string fabricJson)
    {
        var source = Path.Combine(_root, fileName);
        using (var archive = System.IO.Compression.ZipFile.Open(source, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("fabric.mod.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(fabricJson);
        }

        _content.Add("dev", ContentKind.Mods, source);
        File.Delete(source);
    }

    [Fact]
    public void No_mods_means_no_dependencies()
    {
        var result = _checker.Check("dev");

        Assert.Empty(result.Installed);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void Missing_dependency_is_reported()
    {
        AddMod("a.jar", """{"id":"a","version":"1","depends":{"minecraft":"*","cloth-config":">=6"}}""");

        var result = _checker.Check("dev");

        Assert.Contains("a", result.Installed);
        Assert.Equal(["cloth-config"], result.Missing);
    }

    [Fact]
    public void Installed_dependency_is_not_missing()
    {
        AddMod("a.jar", """{"id":"a","version":"1","depends":{"cloth-config":">=6"}}""");
        AddMod("cloth.jar", """{"id":"cloth-config","version":"6","depends":{}}""");

        var result = _checker.Check("dev");

        Assert.Contains("cloth-config", result.Installed);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void Builtins_are_ignored()
    {
        AddMod("a.jar", """{"id":"a","version":"1","depends":{"minecraft":"*","fabricloader":"*","java":"*"}}""");

        Assert.Empty(_checker.Check("dev").Missing);
    }

    [Fact]
    public void Fabric_api_bundle_satisfies_submodule_dependencies()
    {
        AddMod("fabric-api.jar", """{"id":"fabric-api","version":"0.77","depends":{}}""");
        AddMod("jei.jar", """{"id":"jei","version":"10","depends":{"fabric":"*","fabric-key-binding-api-v1":"*"}}""");

        Assert.Empty(_checker.Check("dev").Missing);
    }

    [Fact]
    public void Unknown_jars_are_ignored()
    {
        var source = Path.Combine(_root, "plain.jar");
        File.WriteAllText(source, "not a zip");

        _content.Add("dev", ContentKind.Mods, source);

        var result = _checker.Check("dev");
        Assert.Empty(result.Installed);
        Assert.Empty(result.Missing);
    }
}
