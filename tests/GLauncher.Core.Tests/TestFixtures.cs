using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

/// <summary>读取测试 fixture（随测试程序集一起复制到输出目录）。</summary>
internal static class TestFixtures
{
    public static string Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        Assert.True(File.Exists(path), $"fixture 不存在：{path}");
        return File.ReadAllText(path);
    }
}

/// <summary>显式构造规则求值环境：测试绝不依赖跑测试的机器。</summary>
internal static class TestEnvironment
{
    public static LaunchEnvironment WindowsX64(IReadOnlyDictionary<string, bool>? features = null)
        => new("windows", "x86_64", "10.0.26200", features ?? LaunchEnvironment.NoFeatures);

    public static LaunchEnvironment WindowsX86(IReadOnlyDictionary<string, bool>? features = null)
        => new("windows", "x86", "10.0.26200", features ?? LaunchEnvironment.NoFeatures);

    public static LaunchEnvironment MacArm64(IReadOnlyDictionary<string, bool>? features = null)
        => new("osx", "arm64", "14.5.0", features ?? LaunchEnvironment.NoFeatures);

    public static LaunchEnvironment LinuxX64(IReadOnlyDictionary<string, bool>? features = null)
        => new("linux", "x86_64", "6.8.0", features ?? LaunchEnvironment.NoFeatures);
}
