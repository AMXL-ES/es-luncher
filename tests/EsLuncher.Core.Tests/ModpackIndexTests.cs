using EsLuncher.Core.Content;

namespace EsLuncher.Core.Tests;

public sealed class ModpackIndexTests
{
    [Fact]
    public void Parses_fabric_dependencies()
    {
        const string json = """
        {"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.18.2","fabric-loader":"0.14.21"}}
        """;

        var deps = ModpackIndex.ParseDependencies(json);

        Assert.Equal("1.18.2", deps.MinecraftVersion);
        Assert.Equal("fabric-loader", deps.LoaderKey);
        Assert.Equal("0.14.21", deps.LoaderVersion);
    }

    [Fact]
    public void Parses_forge_dependencies_and_strips_operators()
    {
        const string json = """{"dependencies":{"minecraft":"1.20.1","forge":">=47.2.0"}}""";

        var deps = ModpackIndex.ParseDependencies(json);

        Assert.Equal("forge", deps.LoaderKey);
        Assert.Equal("47.2.0", deps.LoaderVersion);
    }

    [Fact]
    public void Vanilla_pack_has_no_loader()
    {
        const string json = """{"dependencies":{"minecraft":"1.18.2"}}""";

        var deps = ModpackIndex.ParseDependencies(json);

        Assert.Null(deps.LoaderKey);
        Assert.Null(deps.LoaderVersion);
    }

    [Theory]
    [InlineData("""{"dependencies":{}}""")]
    [InlineData("""{"game":"minecraft"}""")]
    public void Missing_minecraft_is_rejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ModpackIndex.ParseDependencies(json));
    }
}
