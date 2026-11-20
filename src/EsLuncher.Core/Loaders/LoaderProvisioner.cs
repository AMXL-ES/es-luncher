using System.Text.Json;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Loaders;

public enum LoaderKind
{
    Fabric,
    Quilt,
    Forge,
    NeoForge,
}

public sealed record LoaderVersion(string Version, bool Stable);

public static class LoaderMeta
{
    public static Uri VersionList(LoaderKind kind, string mcVersion) => kind switch
    {
        LoaderKind.Fabric => new($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}"),
        LoaderKind.Quilt => new($"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}"),
        LoaderKind.Forge => new("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"),
        LoaderKind.NeoForge => new("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static Uri ProfileJson(LoaderKind kind, string mcVersion, string loaderVersion) => kind switch
    {
        LoaderKind.Fabric => new($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json"),
        LoaderKind.Quilt => new($"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}/{loaderVersion}/profile/json"),
        _ => throw new NotSupportedException($"{kind} 不是 meta API 加载器"),
    };

    public static Uri InstallerJar(LoaderKind kind, string mcVersion, string loaderVersion) => kind switch
    {
        LoaderKind.Forge => new(
            $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{loaderVersion}/forge-{mcVersion}-{loaderVersion}-installer.jar"),
        LoaderKind.NeoForge => new(
            $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar"),
        _ => throw new NotSupportedException($"{kind} 不是 installer 型加载器"),
    };
}

public static class LoaderMetaParser
{
    /// <summary>Fabric / Quilt 的版本列表形状：<c>[{"loader":{"version":"0.20.1","stable":true}}, …]</c>。</summary>
    public static IReadOnlyList<LoaderVersion> ParseLoaderList(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new MetadataFormatException("$", "加载器版本列表应为数组");
        }

        var versions = new List<LoaderVersion>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("loader", out var loader) || loader.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var version = loader.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            var stable = loader.TryGetProperty("stable", out var s) && s.ValueKind == JsonValueKind.True;
            versions.Add(new LoaderVersion(version!, stable));
        }

        return versions;
    }

    /// <summary>Forge promotions：<c>promos["1.18.2-recommended"]</c> / <c>["1.18.2-latest"]</c>。</summary>
    public static IReadOnlyList<LoaderVersion> ParseForgePromotions(string json, string mcVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("promos", out var promos) || promos.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$.promos", "缺少 promos");
        }

        var versions = new List<LoaderVersion>();
        foreach (var (suffix, stable) in new[] { ("-recommended", true), ("-latest", false) })
        {
            if (promos.TryGetProperty(mcVersion + suffix, out var value) && value.ValueKind == JsonValueKind.String)
            {
                versions.Add(new LoaderVersion(value.GetString()!, stable));
            }
        }

        return versions;
    }
}

/// <summary>
/// 加载器安装：
/// Fabric / Quilt 走官方 meta API 直出 profile JSON；
/// Forge / NeoForge 下载官方 installer 并执行 <c>--installClient</c>。
/// </summary>
public sealed class LoaderProvisioner(DataLayout layout, FileDownloader downloader, IRemoteFileClient client)
{
    public async Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(
        LoaderKind kind,
        string mcVersion,
        CancellationToken cancellationToken)
    {
        if (kind is LoaderKind.NeoForge)
        {
            throw new NotSupportedException("NeoForge 版本列表未实现（M6 后续）；可用 --loader-version 显式指定");
        }

        var json = await GetStringAsync(LoaderMeta.VersionList(kind, mcVersion), cancellationToken).ConfigureAwait(false);
        return kind == LoaderKind.Forge
            ? LoaderMetaParser.ParseForgePromotions(json, mcVersion)
            : LoaderMetaParser.ParseLoaderList(json);
    }

    public async Task<string> InstallAsync(
        LoaderKind kind,
        string mcVersion,
        string? loaderVersion,
        JavaRuntime? java,
        CancellationToken cancellationToken)
    {
        return kind switch
        {
            LoaderKind.Fabric or LoaderKind.Quilt => await InstallMetaLoaderAsync(kind, mcVersion, loaderVersion, cancellationToken)
                .ConfigureAwait(false),
            LoaderKind.Forge or LoaderKind.NeoForge => await InstallWithInstallerAsync(kind, mcVersion, loaderVersion, java, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private async Task<string> InstallMetaLoaderAsync(
        LoaderKind kind,
        string mcVersion,
        string? loaderVersion,
        CancellationToken cancellationToken)
    {
        var versions = await GetVersionsAsync(kind, mcVersion, cancellationToken).ConfigureAwait(false);
        var chosen = loaderVersion
            ?? versions.FirstOrDefault(version => version.Stable)?.Version
            ?? versions.FirstOrDefault()?.Version
            ?? throw new InvalidOperationException($"{kind} 没有可用于 {mcVersion} 的加载器版本");

        var json = await GetStringAsync(LoaderMeta.ProfileJson(kind, mcVersion, chosen), cancellationToken).ConfigureAwait(false);
        var profile = VersionMetadataParser.Parse(json);
        if (string.IsNullOrWhiteSpace(profile.InheritsFrom))
        {
            throw new InvalidDataException($"加载器 profile 缺少 inheritsFrom：{profile.Id}");
        }

        Directory.CreateDirectory(layout.VersionDir(profile.Id));
        await File.WriteAllTextAsync(layout.VersionJson(profile.Id), json, cancellationToken).ConfigureAwait(false);
        return profile.Id;
    }

    private async Task<string> InstallWithInstallerAsync(
        LoaderKind kind,
        string mcVersion,
        string? loaderVersion,
        JavaRuntime? java,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            var versions = await GetVersionsAsync(kind, mcVersion, cancellationToken).ConfigureAwait(false);
            loaderVersion = versions.FirstOrDefault(version => version.Stable)?.Version
                ?? versions.FirstOrDefault()?.Version
                ?? throw new InvalidOperationException($"{kind} 没有可用于 {mcVersion} 的安装版本");
        }

        if (java is null)
        {
            throw new InvalidOperationException($"{kind} 安装需要 Java（先 `esluncher java install --version {mcVersion}`）");
        }

        var installer = LoaderMeta.InstallerJar(kind, mcVersion, loaderVersion);
        var installerPath = Path.Combine(layout.Root, "cache", "installers", $"{kind}-{mcVersion}-{loaderVersion}.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

        // Forge installer 要求目标目录里存在 launcher_profiles.json，否则直接报错退出。
        var profilesPath = Path.Combine(layout.Root, "launcher_profiles.json");
        if (!File.Exists(profilesPath))
        {
            await File.WriteAllTextAsync(
                    profilesPath,
                    """{"profiles":{},"settings":{},"version":3}""",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var download = await downloader.DownloadAsync(
                new DownloadJob { Primary = installer, TargetPath = installerPath },
                cancellationToken)
            .ConfigureAwait(false);
        if (download.Status == FileDownloadStatus.Failed)
        {
            throw new InvalidOperationException($"{kind} installer 下载失败：{download.Error}");
        }

        var result = await ProcessRunner.RunCapturedAsync(
                java.JavaExePath,
                ["-jar", installerPath, "--installClient", layout.Root],
                TimeSpan.FromMinutes(10),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{kind} installer 退出码 {result.ExitCode}：{result.StandardError}");
        }

        var fragment = kind == LoaderKind.Forge ? $"-forge-{loaderVersion}" : $"-neoforge-{loaderVersion}";
        var installed = Directory.EnumerateDirectories(layout.VersionsDir)
            .Select(Path.GetFileName)
            .FirstOrDefault(name => name is not null && name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        return installed
            ?? throw new InvalidOperationException($"installer 执行完成但没找到版本目录（期望包含 {fragment}）");
    }

    private async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, 0, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Content is null)
        {
            throw new HttpRequestException($"请求失败 HTTP {response.StatusCode}：{uri}");
        }

        using var reader = new StreamReader(response.Content);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
