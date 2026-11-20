using EsLuncher.Core.Loaders;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public class VersionMergerTests
{
    private const string FabricLike = """
    {"id":"fabric-loader-0.20.1-1.18.2","inheritsFrom":"1.18.2","type":"release",
     "mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
     "arguments":{"game":["--fabricFlag"],"jvm":["-DFabricMcEmu=net.minecraft.client.main.Main"]},
     "libraries":[
       {"name":"net.fabricmc:fabric-loader:0.20.1",
        "downloads":{"artifact":{"path":"net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar","sha1":"0000000000000000000000000000000000000000","size":10,"url":"https://maven.fabricmc.net/net/fabricmc/fabric-loader/0.20.1/fabric-loader-0.20.1.jar"}}}
     ]}
    """;

    private static VersionMetadata Parent() => VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

    [Fact]
    public void Child_overrides_mainclass_and_appends_libraries_after_parent()
    {
        var parent = Parent();
        var child = VersionMetadataParser.Parse(FabricLike);

        var merged = VersionMerger.Merge(parent, child);

        Assert.Equal(child.MainClass, merged.MainClass);
        Assert.Equal("fabric-loader-0.20.1-1.18.2", merged.Id);
        Assert.Null(merged.InheritsFrom);
        Assert.Equal(parent.Libraries.Count + 1, merged.Libraries.Count);
        Assert.Equal(parent.Libraries[0].Name, merged.Libraries[0].Name);
        Assert.Equal("net.fabricmc:fabric-loader:0.20.1", merged.Libraries[^1].Name);
        Assert.Equal("26.3", merged.ClientJarVersionId);
    }

    [Fact]
    public void Parent_assets_and_java_version_are_inherited()
    {
        var merged = VersionMerger.Merge(Parent(), VersionMetadataParser.Parse(FabricLike));

        Assert.Equal("34", merged.Assets);
        Assert.Equal(487810291, merged.AssetIndex!.TotalSize);
        Assert.Equal(25, merged.JavaVersion!.MajorVersion);
        Assert.NotNull(merged.Client);
    }

    [Fact]
    public void Arguments_are_concatenated_parent_first()
    {
        var merged = VersionMerger.Merge(Parent(), VersionMetadataParser.Parse(FabricLike));

        Assert.Equal("--username", merged.Arguments!.Game[0].Values[0]);
        Assert.Equal("--fabricFlag", merged.Arguments.Game[^1].Values[0]);
        Assert.Contains(merged.Arguments.Jvm, entry => entry.Values.Contains("-DFabricMcEmu=net.minecraft.client.main.Main"));
    }

    [Fact]
    public void Resolve_follows_inherits_chain()
    {
        var child = VersionMetadataParser.Parse(FabricLike);
        var parent = Parent();

        var merged = VersionMerger.Resolve(child, id => id == "1.18.2" ? parent : throw new InvalidOperationException(id));

        Assert.Equal(child.MainClass, merged.MainClass);
        Assert.Equal("26.3", merged.ClientJarVersionId);
        Assert.Equal(parent.Libraries.Count + 1, merged.Libraries.Count);
    }

    [Fact]
    public void Resolve_rejects_cycles_and_missing_parents()
    {
        var child = VersionMetadataParser.Parse(FabricLike);

        Assert.Throws<InvalidOperationException>(() => VersionMerger.Resolve(child, _ => child, maxDepth: 3));
        Assert.Throws<InvalidOperationException>(() => VersionMerger.Resolve(child, _ => throw new InvalidOperationException("missing")));
    }
}

public class LoaderMetaTests
{
    [Fact]
    public void Parses_fabric_loader_list()
    {
        const string json = """
        [{"loader":{"version":"0.20.1","stable":true},"intermediary":{"version":"1.18.2"},"launcherMeta":{"version":1}},
         {"loader":{"version":"0.20.0","stable":true},"intermediary":{"version":"1.18.2"}},
         {"loader":{"version":"0.15.0-beta.1","stable":false},"intermediary":{"version":"1.18.2"}}]
        """;

        var versions = LoaderMetaParser.ParseLoaderList(json);

        Assert.Equal(3, versions.Count);
        Assert.Equal("0.20.1", versions[0].Version);
        Assert.True(versions[0].Stable);
        Assert.False(versions[2].Stable);
    }

    [Fact]
    public void Parses_forge_promotions()
    {
        const string json = """
        {"homepage":"/","promos":{"1.18.2-recommended":"40.3.0","1.18.2-latest":"40.3.12","1.20.1-latest":"47.4.29"}}
        """;

        var versions = LoaderMetaParser.ParseForgePromotions(json, "1.18.2");

        Assert.Equal(2, versions.Count);
        Assert.Equal("40.3.0", versions[0].Version);
        Assert.True(versions[0].Stable);
        Assert.Equal("40.3.12", versions[1].Version);
    }

    [Fact]
    public void Builds_meta_and_installer_urls()
    {
        var profile = LoaderMeta.ProfileJson(LoaderKind.Fabric, "1.18.2", "0.20.1");
        Assert.Equal("https://meta.fabricmc.net/v2/versions/loader/1.18.2/0.20.1/profile/json", profile.AbsoluteUri);

        var forge = LoaderMeta.InstallerJar(LoaderKind.Forge, "1.18.2", "40.3.0");
        Assert.Equal(
            "https://maven.minecraftforge.net/net/minecraftforge/forge/1.18.2-40.3.0/forge-1.18.2-40.3.0-installer.jar",
            forge.AbsoluteUri);
    }
}
