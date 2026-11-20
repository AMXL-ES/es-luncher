using System.Runtime.InteropServices;
using System.Text.Json;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Java;

/// <summary>
/// Mojang 官方运行时目录（<c>all.json</c>）。实测（2026-11-19）：
/// 平台键有 gamecore/linux/linux-i386/mac-os/mac-os-arm64/windows-arm64/windows-x64/windows-x86，
/// windows-x64 下有 jre-legacy / java-runtime-alpha..epsilon 等组件。
/// </summary>
public static class JavaPlatform
{
    public static string Detect()
    {
        var isWindows = OperatingSystem.IsWindows();
        var isMac = OperatingSystem.IsMacOS();

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 when isWindows => "windows-x64",
            Architecture.X86 when isWindows => "windows-x86",
            Architecture.Arm64 when isWindows => "windows-arm64",
            Architecture.Arm64 when isMac => "mac-os-arm64",
            Architecture.X64 when isMac => "mac-os",
            Architecture.X86 => "linux-i386",
            _ => "linux",
        };
    }
}

public sealed record JavaRuntimePackage(string Component, string VersionName, Uri ManifestUri, string ManifestSha1);

public static class JavaRuntimeCatalog
{
    public static readonly Uri AllRuntimes =
        new("https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json");

    public static IReadOnlyList<JavaRuntimePackage> Parse(string json, string platform)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$", "运行时目录应为 object");
        }

        if (!root.TryGetProperty(platform, out var platformElement) || platformElement.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException($"$.{platform}", $"运行时目录里没有平台 '{platform}'");
        }

        var packages = new List<JavaRuntimePackage>();
        foreach (var component in platformElement.EnumerateObject())
        {
            if (component.Value.ValueKind != JsonValueKind.Array || component.Value.GetArrayLength() == 0)
            {
                continue;
            }

            var first = component.Value[0];
            var manifest = JsonRead.RequiredObject(first, "manifest", $"$.{platform}.{component.Name}[0]");
            var version = JsonRead.RequiredObject(first, "version", $"$.{platform}.{component.Name}[0]");

            packages.Add(new JavaRuntimePackage(
                Component: component.Name,
                VersionName: JsonRead.RequiredString(version, "name", $"$.{platform}.{component.Name}[0].version"),
                ManifestUri: new Uri(JsonRead.RequiredString(manifest, "url", $"$.{platform}.{component.Name}[0].manifest")),
                ManifestSha1: JsonRead.RequiredString(manifest, "sha1", $"$.{platform}.{component.Name}[0].manifest")));
        }

        return packages;
    }

    public static JavaRuntimePackage? Find(IReadOnlyList<JavaRuntimePackage> packages, string component)
        => packages.FirstOrDefault(package => string.Equals(package.Component, component, StringComparison.OrdinalIgnoreCase));
}
