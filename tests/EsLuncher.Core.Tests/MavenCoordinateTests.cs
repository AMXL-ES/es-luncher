using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public class MavenCoordinateTests
{
    [Fact]
    public void Parses_plain_coordinate()
    {
        var coordinate = MavenCoordinate.Parse("org.lwjgl.lwjgl:lwjgl-platform:2.9.4-nightly-20150209");

        Assert.Equal("org.lwjgl.lwjgl", coordinate.Group);
        Assert.Equal("lwjgl-platform", coordinate.Artifact);
        Assert.Equal("2.9.4-nightly-20150209", coordinate.Version);
        Assert.Null(coordinate.Classifier);
        Assert.Equal("jar", coordinate.Extension);
        Assert.Equal(
            "org/lwjgl/lwjgl/lwjgl-platform/2.9.4-nightly-20150209/lwjgl-platform-2.9.4-nightly-20150209.jar",
            coordinate.ToMavenPath());
    }

    [Fact]
    public void Parses_classifier_coordinate()
    {
        var coordinate = MavenCoordinate.Parse("com.mojang:jtracy:1.14.38:natives-linux");

        Assert.Equal("natives-linux", coordinate.Classifier);
        Assert.Equal(
            "com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-linux.jar",
            coordinate.ToMavenPath());
        Assert.Equal(
            "com/mojang/jtracy/1.14.38/jtracy-1.14.38-linux.jar",
            coordinate.ToMavenPath("linux"));
    }

    [Fact]
    public void Parses_extension_override()
    {
        var coordinate = MavenCoordinate.Parse("a.b:c:1.0@zip");

        Assert.Equal("zip", coordinate.Extension);
        Assert.EndsWith("c-1.0.zip", coordinate.ToMavenPath());
    }

    [Fact]
    public void Rejects_malformed_coordinate()
    {
        Assert.Throws<MetadataFormatException>(() => MavenCoordinate.Parse("not-a-coordinate"));
        Assert.Throws<MetadataFormatException>(() => MavenCoordinate.Parse(string.Empty));
    }
}
