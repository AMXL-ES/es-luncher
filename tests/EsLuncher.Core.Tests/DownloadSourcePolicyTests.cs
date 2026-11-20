using EsLuncher.Core.Downloads;

namespace EsLuncher.Core.Tests;

public sealed class DownloadSourcePolicyTests
{
    [Fact]
    public void Official_source_leaves_uris_unchanged()
    {
        DownloadSourcePolicy.Source = DownloadSource.Official;
        var uri = new Uri("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

        Assert.Equal(uri, DownloadSourcePolicy.Rewrite(uri));
    }

    [Fact]
    public void Bmclapi_rewrites_manifests_libraries_and_assets()
    {
        DownloadSourcePolicy.Source = DownloadSource.Bmclapi;
        try
        {
            var meta = DownloadSourcePolicy.Rewrite(new Uri("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
            Assert.Equal("bmclapi2.bangbang93.com", meta.Host);
            Assert.Equal("/mc/game/version_manifest_v2.json", meta.AbsolutePath);

            var lib = DownloadSourcePolicy.Rewrite(new Uri("https://libraries.minecraft.net/com/example/a/1.0/a-1.0.jar"));
            Assert.Equal("/maven/com/example/a/1.0/a-1.0.jar", lib.AbsolutePath);

            var asset = DownloadSourcePolicy.Rewrite(new Uri("http://resources.download.minecraft.net/ab/abcdef"));
            Assert.Equal("/assets/ab/abcdef", asset.AbsolutePath);
        }
        finally
        {
            DownloadSourcePolicy.Source = DownloadSource.Official;
        }
    }

    [Fact]
    public void Launcher_mojang_is_left_official()
    {
        DownloadSourcePolicy.Source = DownloadSource.Bmclapi;
        try
        {
            var uri = new Uri("https://launcher.mojang.com/v1/objects/abc/client.jar");
            Assert.Equal(uri, DownloadSourcePolicy.Rewrite(uri));
        }
        finally
        {
            DownloadSourcePolicy.Source = DownloadSource.Official;
        }
    }
}
