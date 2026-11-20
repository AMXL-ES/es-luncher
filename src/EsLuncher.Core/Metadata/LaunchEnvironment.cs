using System.Runtime.InteropServices;

namespace EsLuncher.Core.Metadata;

/// <summary>
/// 规则求值用的"当前环境"。测试里显式构造，运行期用 <see cref="Detect"/>。
/// OsName 取值与 Mojang 元数据一致：<c>windows</c> / <c>osx</c> / <c>linux</c>。
/// </summary>
public sealed record LaunchEnvironment(
    string OsName,
    string OsArch,
    string OsVersion,
    IReadOnlyDictionary<string, bool> Features)
{
    public static readonly IReadOnlyDictionary<string, bool> NoFeatures = new Dictionary<string, bool>(0);

    /// <summary>
    /// natives 模板里 <c>${arch}</c> 的取值：32 / 64（实测 1.7.10 的 twitch 库用
    /// <c>natives-windows-32</c> / <c>natives-windows-64</c>）。与 rules 的 <c>os.arch</c> 不同。
    /// </summary>
    public string NativeArch => OsArch switch
    {
        "x86" => "32",
        "x86_64" => "64",
        "arm" => "32",
        "arm64" => "arm64",
        _ => OsArch,
    };

    public static LaunchEnvironment Detect(IReadOnlyDictionary<string, bool>? features = null)
    {
        var osName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "osx"
                : "linux";

        var osArch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

        return new LaunchEnvironment(osName, osArch, Environment.OSVersion.Version.ToString(), features ?? NoFeatures);
    }
}
