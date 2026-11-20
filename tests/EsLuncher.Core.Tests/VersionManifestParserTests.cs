using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public class VersionManifestParserTests
{
    [Fact]
    public void Parses_fixture_with_all_version_types()
    {
        var manifest = VersionManifestParser.Parse(TestFixtures.Load("manifest.min.json"));

        Assert.Equal("26.3", manifest.Latest.Release);
        Assert.Equal("26.4-snapshot-10", manifest.Latest.Snapshot);
        Assert.Equal(5, manifest.Versions.Count);
        Assert.Contains(manifest.Versions, v => v.Type == "old_alpha");

        var release = manifest.Versions[0];
        Assert.Equal("26.3", release.Id);
        Assert.Equal("release", release.Type);
        Assert.Equal("ead839cbccf0ddf013c3f935f2840080b5cb0d26", release.Sha1);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 11, 23, 2, TimeSpan.Zero), release.ReleaseTime);
        Assert.Equal(1, release.ComplianceLevel);
        Assert.StartsWith("https://piston-meta.mojang.com/", release.Url);
    }

    [Fact]
    public void Missing_url_reports_json_path()
    {
        const string json = """
        {"latest":{"release":"a","snapshot":"b"},"versions":[{"id":"x","type":"release","sha1":"00"}]}
        """;

        var exception = Assert.Throws<MetadataFormatException>(() => VersionManifestParser.Parse(json));

        Assert.Contains("$.versions[0].url", exception.Path);
    }

    [Fact]
    public void Non_object_root_is_rejected()
    {
        var exception = Assert.Throws<MetadataFormatException>(() => VersionManifestParser.Parse("[]"));

        Assert.Equal("$", exception.Path);
    }
}
