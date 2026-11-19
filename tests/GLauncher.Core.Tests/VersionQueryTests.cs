using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public class VersionQueryTests
{
    private static VersionManifest Manifest() => VersionManifestParser.Parse(TestFixtures.Load("manifest.min.json"));

    [Fact]
    public void Filters_by_type_in_manifest_order()
    {
        var releases = VersionQuery.Latest(Manifest(), "release", 10);

        Assert.Equal(3, releases.Count);
        Assert.Equal("26.3", releases[0].Id);
        Assert.Equal("1.12.2", releases[1].Id);
        Assert.Equal("1.7.10", releases[2].Id);
    }

    [Fact]
    public void All_returns_every_type()
    {
        var all = VersionQuery.Latest(Manifest(), "all", 10);

        Assert.Equal(5, all.Count);
        Assert.Contains(all, entry => entry.Type == "old_alpha");
        Assert.Contains(all, entry => entry.Type == "snapshot");
    }

    [Fact]
    public void Limit_is_respected()
    {
        Assert.Single(VersionQuery.Latest(Manifest(), "all", 1));
    }

    [Fact]
    public void Unknown_type_returns_empty()
    {
        Assert.Empty(VersionQuery.Latest(Manifest(), "pre_release", 10));
    }

    [Fact]
    public void Find_locates_entry_by_id()
    {
        Assert.Equal("1.7.10", VersionQuery.Find(Manifest(), "1.7.10")!.Id);
        Assert.Null(VersionQuery.Find(Manifest(), "9.9.9"));
    }

    [Fact]
    public void Invalid_count_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VersionQuery.Latest(Manifest(), "all", 0));
    }
}
