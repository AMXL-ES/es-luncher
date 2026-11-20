using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Tests;

public sealed class InstanceGroupTests : IDisposable
{
    private readonly string _root;
    private readonly InstanceStore _instances;

    public InstanceGroupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-group-tests", Guid.NewGuid().ToString("N"));
        _instances = new InstanceStore(new DataLayout(_root));
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
    public void Group_roundtrips_through_config()
    {
        var config = _instances.TryLoad("dev")!;
        _instances.Save(config with { Group = "Fabric 系列" });

        Assert.Equal("Fabric 系列", _instances.TryLoad("dev")!.Group);
    }
}

public sealed class ResourcePackOrderTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly InstanceStore _instances;
    private readonly ResourcePackOrder _order;

    public ResourcePackOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-order-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _instances = new InstanceStore(_layout);
        _instances.Create("dev", "1.18.2");
        _order = new ResourcePackOrder(_layout, _instances);
        Directory.CreateDirectory(_layout.GameDir("dev"));
        File.WriteAllText(
            Path.Combine(_layout.GameDir("dev"), "options.txt"),
            "fov:90\nresourcePacks:[\"vanilla\",\"file/a.zip\",\"file/b.zip\"]\nmouseSensitivity:0.5\n");
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
    public void Lists_packs_in_options_order()
    {
        Assert.Equal(["vanilla", "file/a.zip", "file/b.zip"], _order.List("dev"));
    }

    [Fact]
    public void Moves_pack_up_and_preserves_other_keys()
    {
        Assert.True(_order.Move("dev", "file/b.zip", -1));

        Assert.Equal(["vanilla", "file/b.zip", "file/a.zip"], _order.List("dev"));
        var options = File.ReadAllText(Path.Combine(_layout.GameDir("dev"), "options.txt"));
        Assert.Contains("fov:90", options);
        Assert.Contains("mouseSensitivity:0.5", options);
        Assert.True(File.Exists(Path.Combine(_layout.GameDir("dev"), "options.txt.bak")));
    }

    [Fact]
    public void Cannot_move_past_ends()
    {
        Assert.False(_order.Move("dev", "vanilla", -1));
        Assert.False(_order.Move("dev", "file/b.zip", 1));
    }

    [Fact]
    public void Missing_options_returns_empty()
    {
        _instances.Create("empty", "1.18.2");
        Assert.Empty(_order.List("empty"));
    }
}
