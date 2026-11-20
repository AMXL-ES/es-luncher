using EsLuncher.Core.Downloads;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public sealed class ClasspathBuilderTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;

    public ClasspathBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-cp-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
    }

    public void Dispose()
    {
        // 纯路径计算，不落盘
    }

    [Fact]
    public void Modern_version_puts_common_library_before_client_jar()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

        var classpath = ClasspathBuilder.Build(version, TestEnvironment.WindowsX64(), _layout);

        Assert.Equal(2, classpath.Count);
        Assert.EndsWith(Path.Combine("com", "mojang", "brigadier", "1.3.10", "brigadier-1.3.10.jar"), classpath[0]);
        Assert.Equal(_layout.ClientJar("26.3"), classpath[1]);
    }

    [Fact]
    public void Legacy_natives_jars_stay_out_of_classpath()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.12.2.min.json"));

        var classpath = ClasspathBuilder.Build(version, TestEnvironment.WindowsX64(), _layout);

        Assert.Equal(3, classpath.Count);
        Assert.EndsWith("lwjgl-platform-2.9.4-nightly-20150209.jar", classpath[0]);
        Assert.EndsWith("jinput-2.0.5.jar", classpath[1]);
        Assert.Equal(_layout.ClientJar("1.12.2"), classpath[2]);
        Assert.DoesNotContain(classpath, path => path.Contains("natives-windows", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Filtered_libraries_are_excluded()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

        var windows = ClasspathBuilder.Build(version, TestEnvironment.WindowsX64(), _layout);
        var linux = ClasspathBuilder.Build(version, TestEnvironment.LinuxX64(), _layout);

        Assert.Equal(2, windows.Count);
        Assert.DoesNotContain(windows, path => path.Contains("jtracy", StringComparison.OrdinalIgnoreCase));

        // Linux 上 jtracy:natives-linux 被规则允许并进 classpath，Windows/OSX 专用库被排除
        Assert.Equal(3, linux.Count);
        Assert.Contains(linux, path => path.Contains("jtracy-1.14.38-natives-linux.jar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Library_with_only_base_url_is_included()
    {
        // Fabric/Quilt profile 只给 name + url（没有 downloads.artifact），必须在 classpath 里。
        const string json = """
        {"id":"fabric-loader-0.20.1-1.18.2","inheritsFrom":"1.18.2","type":"release",
         "mainClass":"net.fabricmc.loader.impl.launch.knot.KnotClient",
         "libraries":[{"name":"net.fabricmc:fabric-loader:0.20.1","url":"https://maven.fabricmc.net/"}]}
        """;
        var version = VersionMetadataParser.Parse(json);

        var classpath = ClasspathBuilder.Build(version, TestEnvironment.WindowsX64(), _layout);

        Assert.Contains(classpath, path => path.EndsWith("fabric-loader-0.20.1.jar", StringComparison.Ordinal));
    }

    [Fact]
    public void BootstrapLauncher_versions_exclude_vanilla_jar()
    {
        // Forge/NeoForge 1.17+ 的 client jar 由 srg/module 层加载，不能再进 -cp。
        const string json = """
        {"id":"1.18.2-forge-40.3.0","inheritsFrom":"1.18.2","type":"release",
         "mainClass":"cpw.mods.bootstraplauncher.BootstrapLauncher",
         "libraries":[{"name":"net.minecraftforge:fmlloader:1.18.2-40.3.0","url":"https://maven.minecraftforge.net/"}]}
        """;
        var version = VersionMetadataParser.Parse(json);

        var classpath = ClasspathBuilder.Build(version, TestEnvironment.WindowsX64(), _layout);

        Assert.DoesNotContain(classpath, path => path.EndsWith("1.18.2.jar", StringComparison.OrdinalIgnoreCase) && path.Contains("versions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(classpath, path => path.EndsWith("fmlloader-1.18.2-40.3.0.jar", StringComparison.Ordinal));
    }
}
