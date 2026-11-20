using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public class VersionMetadataParserTests
{
    private static VersionMetadata Parse(string fixture) => VersionMetadataParser.Parse(TestFixtures.Load(fixture));

    [Fact]
    public void Parses_modern_fixture()
    {
        var version = Parse("26.3.min.json");

        Assert.Equal("26.3", version.Id);
        Assert.Equal("release", version.Type);
        Assert.Equal("net.minecraft.client.main.Main", version.MainClass);
        Assert.Equal(25, version.JavaVersion!.MajorVersion);
        Assert.Equal("java-runtime-epsilon", version.JavaVersion.Component);
        Assert.Null(version.InheritsFrom);
        Assert.Null(version.LegacyMinecraftArguments);
        Assert.NotNull(version.Arguments);
        Assert.Equal(3, version.Libraries.Count);
        Assert.Equal(41483720L, version.Client!.Size);
        Assert.Equal(487810291L, version.AssetIndex!.TotalSize);
        Assert.NotNull(version.Logging);
        Assert.Equal("-Dlog4j.configurationFile=${path}", version.Logging!.Argument);
        Assert.Equal(21, version.MinimumLauncherVersion);
    }

    [Fact]
    public void Windows_environment_keeps_only_matching_libraries()
    {
        var version = Parse("26.3.min.json");

        var allowed = version.Libraries
            .Where(library => library.IsAllowed(TestEnvironment.WindowsX64()))
            .Select(library => library.Name)
            .ToArray();

        Assert.Single(allowed);
        Assert.Equal("com.mojang:brigadier:1.3.10", allowed[0]);
    }

    [Fact]
    public void Mac_environment_keeps_osx_and_common_libraries()
    {
        var version = Parse("26.3.min.json");

        var allowed = version.Libraries
            .Where(library => library.IsAllowed(TestEnvironment.MacArm64()))
            .Select(library => library.Name)
            .ToArray();

        Assert.Equal(2, allowed.Length);
        Assert.Contains("ca.weblite:java-objc-bridge:1.1", allowed);
    }

    [Fact]
    public void Parses_legacy_fixture_with_natives()
    {
        var version = Parse("1.12.2.min.json");

        Assert.NotNull(version.LegacyMinecraftArguments);
        Assert.Null(version.Arguments);
        Assert.Equal(2, version.Libraries.Count);

        var nativesLibrary = version.Libraries[0];
        Assert.Equal("natives-windows", nativesLibrary.ResolveNativeClassifier("windows", "64"));
        Assert.Equal("natives-linux", nativesLibrary.ResolveNativeClassifier("linux", "64"));
        Assert.Null(nativesLibrary.ResolveNativeClassifier("osx", "arm64"));
        Assert.Contains("META-INF/", nativesLibrary.ExtractExclude);
        Assert.Contains("natives-windows", nativesLibrary.Classifiers.Keys);
        Assert.True(nativesLibrary.IsAllowed(TestEnvironment.WindowsX64()));
        Assert.False(nativesLibrary.IsAllowed(TestEnvironment.MacArm64()));
    }

    [Fact]
    public void Parses_oldest_fixture()
    {
        var version = Parse("1.7.10.min.json");

        Assert.Equal(8, version.JavaVersion!.MajorVersion);
        Assert.NotNull(version.LegacyMinecraftArguments);
        Assert.Equal(2, version.Libraries.Count);
        Assert.Null(version.Arguments);
    }

    [Fact]
    public void Legacy_library_without_downloads_keeps_base_url()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","minecraftArguments":"--demo",
         "libraries":[{"name":"org.lwjgl:lwjgl:2.9.1","url":"https://libraries.minecraft.net/"}]}
        """;

        var version = VersionMetadataParser.Parse(json);

        Assert.Null(version.Libraries[0].Artifact);
        Assert.Equal("https://libraries.minecraft.net/", version.Libraries[0].BaseUrl);
    }

    [Fact]
    public void Unknown_rule_action_reports_json_path()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m",
         "libraries":[{"name":"a:b:1","rules":[{"action":"deny"}]}]}
        """;

        var exception = Assert.Throws<MetadataFormatException>(() => VersionMetadataParser.Parse(json));

        Assert.Contains("$.libraries[0].rules[0].action", exception.Path);
    }

    [Fact]
    public void Missing_library_name_reports_json_path()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","libraries":[{}]}
        """;

        var exception = Assert.Throws<MetadataFormatException>(() => VersionMetadataParser.Parse(json));

        Assert.Contains("$.libraries[0].name", exception.Path);
    }
}
