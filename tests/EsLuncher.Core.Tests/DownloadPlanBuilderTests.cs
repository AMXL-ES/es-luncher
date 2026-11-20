using EsLuncher.Core.Downloads;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public sealed class DownloadPlanBuilderTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly DownloadPlanBuilder _builder;

    public DownloadPlanBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-plan-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _builder = new DownloadPlanBuilder(_layout);
    }

    public void Dispose()
    {
        // 计划生成不落盘，这里只需保证临时目录不存在
    }

    [Fact]
    public void Modern_version_plan_has_client_index_logging_and_matching_library()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

        var plan = _builder.Build(version, TestEnvironment.WindowsX64(), "26.3");

        Assert.Equal(4, plan.Files.Count); // client + asset index + logging + brigadier
        Assert.Empty(plan.Natives);

        var client = plan.Files.Single(job => job.TargetPath == _layout.ClientJar("26.3"));
        Assert.Equal(41483720L, client.ExpectedSize);
        Assert.Contains(client.Mirrors, m => m.Host == "bmclapi2.bangbang93.com");

        var index = plan.Files.Single(job => job.TargetPath == _layout.IndexFile("34"));
        Assert.Equal(597235L, index.ExpectedSize);

        var library = plan.Files.Single(job => job.TargetPath.EndsWith("brigadier-1.3.10.jar", StringComparison.Ordinal));
        Assert.Contains(library.Mirrors, m => m.AbsolutePath.Contains("/maven/", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_version_plan_includes_native_extraction()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.12.2.min.json"));

        var plan = _builder.Build(version, TestEnvironment.WindowsX64(), "1.12.2");

        var native = Assert.Single(plan.Natives);
        Assert.EndsWith("lwjgl-platform-2.9.4-nightly-20150209-natives-windows.jar", native.JarPath);
        Assert.Equal(Path.Combine(_root, "instances", "1.12.2", "natives"), native.TargetDirectory);
        Assert.Contains("META-INF/", native.ExcludePrefixes);

        Assert.Contains(plan.Files, job => job.TargetPath == native.JarPath);
        Assert.Contains(plan.Files, job => job.TargetPath.EndsWith("jinput-2.0.5.jar", StringComparison.Ordinal));
    }

    [Fact]
    public void Mac_environment_skips_legacy_natives_because_of_disallow_rule()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.12.2.min.json"));

        var plan = _builder.Build(version, TestEnvironment.MacArm64(), "1.12.2");

        Assert.Empty(plan.Natives);
        Assert.DoesNotContain(plan.Files, job => job.TargetPath.Contains("natives-windows", StringComparison.Ordinal));
    }

    [Fact]
    public void Selection_can_turn_off_parts()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

        var plan = _builder.Build(
            version,
            TestEnvironment.WindowsX64(),
            "26.3",
            new FetchSelection(Client: false, Libraries: false, Assets: false, Natives: false));

        // 只剩日志配置（1 KB，启动要用）
        Assert.Single(plan.Files);
        Assert.Contains("log_configs", plan.Files[0].TargetPath);
    }

    [Fact]
    public void Asset_plan_dedupes_by_hash_and_uses_mirror()
    {
        const string indexJson = """
        {"objects":{
          "a/x.png":{"hash":"bdf48ef6b5d0d23bbb02e17d04865216179f510a","size":3665},
          "b/copy.png":{"hash":"bdf48ef6b5d0d23bbb02e17d04865216179f510a","size":3665},
          "c/y.txt":{"hash":"0f2b6a5c1d3e4f60718293a4b5c6d7e8f9012345","size":10}
        }}
        """;
        var index = AssetIndexParser.Parse(indexJson);

        var plan = _builder.BuildAssets(index);

        Assert.Equal(2, plan.Files.Count);
        var first = plan.Files.Single(job => job.ExpectedSha1 == "bdf48ef6b5d0d23bbb02e17d04865216179f510a");
        Assert.Equal(_layout.AssetObjectPath("bdf48ef6b5d0d23bbb02e17d04865216179f510a"), first.TargetPath);
        Assert.Equal(new Uri("https://resources.download.minecraft.net/bd/bdf48ef6b5d0d23bbb02e17d04865216179f510a"), first.Primary);
        Assert.Contains(first.Mirrors, m => m.AbsolutePath.StartsWith("/assets/", StringComparison.Ordinal));
    }

    [Fact]
    public void Native_archive_placeholder_uses_32_or_64_bits()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","minecraftArguments":"--demo",
         "libraries":[
           {"name":"tv.twitch:twitch-platform:5.16",
            "downloads":{"classifiers":{
              "natives-windows-32":{"path":"tv/twitch/twitch-platform/5.16/twitch-platform-5.16-natives-windows-32.jar","sha1":"bdf48ef6b5d0d23bbb02e17d04865216179f510a","size":10,"url":"https://libraries.minecraft.net/tv/twitch/twitch-platform/5.16/twitch-platform-5.16-natives-windows-32.jar"},
              "natives-windows-64":{"path":"tv/twitch/twitch-platform/5.16/twitch-platform-5.16-natives-windows-64.jar","sha1":"0f2b6a5c1d3e4f60718293a4b5c6d7e8f9012345","size":20,"url":"https://libraries.minecraft.net/tv/twitch/twitch-platform/5.16/twitch-platform-5.16-natives-windows-64.jar"}}},
            "extract":{"exclude":["META-INF/"]},
            "natives":{"windows":"natives-windows-${arch}"}}
         ]}
        """;
        var version = VersionMetadataParser.Parse(json);

        var plan = _builder.Build(version, TestEnvironment.WindowsX64(), "x");

        var native = Assert.Single(plan.Natives);
        Assert.EndsWith("twitch-platform-5.16-natives-windows-64.jar", native.JarPath);
        Assert.Single(plan.Files);
    }

    [Fact]
    public void Library_without_artifact_falls_back_to_base_url()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","minecraftArguments":"--demo",
         "libraries":[{"name":"org.lwjgl:lwjgl:2.9.1","url":"https://libraries.minecraft.net/"}]}
        """;
        var version = VersionMetadataParser.Parse(json);

        var plan = _builder.Build(version, TestEnvironment.WindowsX64(), "x");

        var job = Assert.Single(plan.Files);
        Assert.Equal(
            new Uri("https://libraries.minecraft.net/org/lwjgl/lwjgl/2.9.1/lwjgl-2.9.1.jar"),
            job.Primary);
        Assert.Null(job.ExpectedSha1);
    }
}
