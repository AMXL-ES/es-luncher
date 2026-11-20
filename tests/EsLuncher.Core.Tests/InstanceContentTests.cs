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
        Assert.True(InstanceContentService.TryParseKind("worlds", out var worlds));
        Assert.Equal(ContentKind.Worlds, worlds);
        Assert.True(InstanceContentService.TryParseKind("datapacks", out var datapacks));
        Assert.Equal(ContentKind.Datapacks, datapacks);
        Assert.False(InstanceContentService.TryParseKind("mystery", out _));
    }

    [Fact]
    public void Enable_disable_roundtrip_uses_suffix()
    {
        var source = Path.Combine(_root, "toggle.jar");
        File.WriteAllText(source, "bytes");
        _content.Add("dev", ContentKind.Mods, source);

        Assert.True(_content.SetEnabled("dev", ContentKind.Mods, "toggle.jar", false));
        var modsDir = Path.Combine(_layout.GameDir("dev"), "mods");
        Assert.False(File.Exists(Path.Combine(modsDir, "toggle.jar")));
        Assert.True(File.Exists(Path.Combine(modsDir, "toggle.jar.disabled")));

        var listed = _content.List("dev", ContentKind.Mods);
        Assert.Single(listed);
        Assert.False(listed[0].Enabled);
        Assert.Equal("toggle.jar", listed[0].Name);

        Assert.True(_content.SetEnabled("dev", ContentKind.Mods, "toggle.jar", true));
        Assert.True(File.Exists(Path.Combine(modsDir, "toggle.jar")));
    }

    [Fact]
    public void Worlds_are_directories_in_saves()
    {
        var world = Path.Combine(_layout.GameDir("dev"), "saves", "My World");
        Directory.CreateDirectory(world);
        File.WriteAllText(Path.Combine(world, "level.dat"), "world");

        var list = _content.List("dev", ContentKind.Worlds);

        Assert.Single(list);
        Assert.Equal("My World", list[0].Name);
        Assert.True(list[0].IsDirectory);
    }

    [Fact]
    public void Datapacks_need_a_world()
    {
        Assert.Throws<ArgumentException>(() => _content.List("dev", ContentKind.Datapacks));
    }

    [Fact]
    public void Datapacks_listed_under_selected_world()
    {
        var datapacks = Path.Combine(_layout.GameDir("dev"), "saves", "world1", "datapacks");
        Directory.CreateDirectory(datapacks);
        File.WriteAllText(Path.Combine(datapacks, "pack.zip"), "zip");

        var list = _content.List("dev", ContentKind.Datapacks, "world1");

        Assert.Single(list);
        Assert.Equal("pack.zip", list[0].Name);
    }

    [Fact]
    public void Worlds_cannot_be_toggled()
    {
        Assert.Throws<NotSupportedException>(() => _content.SetEnabled("dev", ContentKind.Worlds, "w", false));
    }

    [Fact]
    public void Remove_finds_disabled_suffix()
    {
        var source = Path.Combine(_root, "off.jar");
        File.WriteAllText(source, "bytes");
        _content.Add("dev", ContentKind.Mods, source);
        _content.SetEnabled("dev", ContentKind.Mods, "off.jar", false);

        Assert.True(_content.Remove("dev", ContentKind.Mods, "off.jar"));
        Assert.Empty(_content.List("dev", ContentKind.Mods));
    }
    [Fact]
    public void Partial_download_files_are_hidden()
    {
        var mods = Path.Combine(_layout.GameDir("dev"), "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(mods, "downloading.jar.part"), "half");

        Assert.Empty(_content.List("dev", ContentKind.Mods));
    }
}