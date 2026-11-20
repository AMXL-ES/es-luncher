using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public sealed class InstanceContentTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly InstanceContentService _content;

    public InstanceContentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-content-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _content = new InstanceContentService(_layout, _instances);
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

    [Fact]
    public void Empty_directories_list_nothing()
    {
        Assert.Empty(_content.List("dev", ContentKind.Mods));
        Assert.Empty(_content.List("dev", ContentKind.ResourcePacks));
    }

    [Fact]
    public void Add_copies_file_and_list_reports_it()
    {
        var source = Path.Combine(_root, "some-mod.jar");
        File.WriteAllText(source, "jar-bytes");

        var added = _content.Add("dev", ContentKind.Mods, source);

        Assert.Equal("some-mod.jar", added.Name);
        Assert.Equal(9, added.Size);
        var listed = _content.List("dev", ContentKind.Mods);
        Assert.Single(listed);
        Assert.Equal("some-mod.jar", listed[0].Name);
        Assert.True(File.Exists(Path.Combine(_layout.GameDir("dev"), "mods", "some-mod.jar")));
    }

    [Fact]
    public void Remove_moves_file_to_trash()
    {
        var source = Path.Combine(_root, "pack.zip");
        File.WriteAllText(source, "zip-bytes");
        _content.Add("dev", ContentKind.ResourcePacks, source);

        Assert.True(_content.Remove("dev", ContentKind.ResourcePacks, "pack.zip"));
        Assert.Empty(_content.List("dev", ContentKind.ResourcePacks));

        var trashed = Directory.EnumerateFiles(Path.Combine(_root, "trash", "content", "dev")).ToArray();
        Assert.Single(trashed);
        Assert.EndsWith("pack.zip", trashed[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_missing_returns_false()
    {
        Assert.False(_content.Remove("dev", ContentKind.Mods, "nope.jar"));
    }

    [Theory]
    [InlineData("../escape.jar")]
    [InlineData("a/b.jar")]
    [InlineData("..")]
    public void Remove_rejects_path_like_names(string fileName)
    {
        Assert.Throws<ArgumentException>(() => _content.Remove("dev", ContentKind.Mods, fileName));
    }

    [Fact]
    public void Add_rejects_missing_source()
    {
        Assert.Throws<FileNotFoundException>(() => _content.Add("dev", ContentKind.Mods, Path.Combine(_root, "nope.jar")));
    }

    [Fact]
    public void Unknown_instance_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => _content.List("nobody", ContentKind.Mods));
    }

    [Fact]
    public void Kinds_parse_from_cli_names()
    {
        Assert.True(InstanceContentService.TryParseKind("mods", out var mods));
        Assert.Equal(ContentKind.Mods, mods);
        Assert.True(InstanceContentService.TryParseKind("resourcepacks", out var packs));
        Assert.Equal(ContentKind.ResourcePacks, packs);
        Assert.False(InstanceContentService.TryParseKind("worlds", out _));
    }
}
