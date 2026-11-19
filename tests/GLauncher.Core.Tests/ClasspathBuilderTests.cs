using GLauncher.Core.Downloads;
using GLauncher.Core.Launching;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public sealed class ClasspathBuilderTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;

    public ClasspathBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "glauncher-cp-tests", Guid.NewGuid().ToString("N"));
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
}
