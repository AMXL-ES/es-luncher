using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public sealed class AssetIndexParserTests
{
    [Fact]
    public void Parses_objects_and_flags()
    {
        const string json = """
        {"virtual":true,"map_to_resources":false,"objects":{
          "icons/icon_16x16.png":{"hash":"bdf48ef6b5d0d23bbb02e17d04865216179f510a","size":3665},
          "minecraft/sounds/a.ogg":{"hash":"0f2b6a5c1d3e4f60718293a4b5c6d7e8f9012345","size":1234}
        }}
        """;

        var index = AssetIndexParser.Parse(json);

        Assert.Equal(2, index.Objects.Count);
        Assert.True(index.Virtual);
        Assert.False(index.MapToResources);
        Assert.Equal(3665, index.Objects["icons/icon_16x16.png"].Size);
        Assert.Equal("bdf48ef6b5d0d23bbb02e17d04865216179f510a", index.Objects["icons/icon_16x16.png"].Hash);
    }

    [Fact]
    public void Rejects_non_sha1_hash()
    {
        const string json = """
        {"objects":{"a/b.png":{"hash":"../../etc/passwd","size":1}}}
        """;

        var exception = Assert.Throws<MetadataFormatException>(() => AssetIndexParser.Parse(json));

        Assert.Contains("hash", exception.Path);
    }

    [Fact]
    public void Rejects_missing_size()
    {
        const string json = """
        {"objects":{"a/b.png":{"hash":"bdf48ef6b5d0d23bbb02e17d04865216179f510a"}}}
        """;

        var exception = Assert.Throws<MetadataFormatException>(() => AssetIndexParser.Parse(json));

        Assert.Contains("size", exception.Path);
    }

    [Fact]
    public void Rejects_missing_objects_block()
    {
        var exception = Assert.Throws<MetadataFormatException>(() => AssetIndexParser.Parse("{}"));

        Assert.Equal("$.objects", exception.Path);
    }
}
