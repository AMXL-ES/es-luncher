using System.IO.Compression;
using System.Text.Json;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Loaders;

namespace EsLuncher.Core.Content;

public sealed record ModpackDependencies(string MinecraftVersion, string? LoaderKey, string? LoaderVersion);

public static class ModpackIndex
{
    /// <summary>从 mrpack 的 modrinth.index.json 读依赖：minecraft 与加载器（fabric-loader/quilt-loader/forge/neoforge）。</summary>
    public static ModpackDependencies ParseDependencies(string indexJson)
    {
        using var document = JsonDocument.Parse(indexJson);
        if (!document.RootElement.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("modrinth.index.json 缺少 dependencies");
        }

        if (!dependencies.TryGetProperty("minecraft", out var minecraft)
            || minecraft.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("modrinth.index.json 缺少 dependencies.minecraft");
        }

        foreach (var key in new[] { "fabric-loader", "quilt-loader", "neoforge", "forge" })
        {
            if (dependencies.TryGetProperty(key, out var loader) && loader.ValueKind == JsonValueKind.String)
            {
                return new ModpackDependencies(minecraft.GetString()!, key, NormalizeVersion(loader.GetString()));
            }
        }

        return new ModpackDependencies(minecraft.GetString()!, null, null);
    }

    private static string? NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        while (trimmed.Length > 0 && (trimmed[0] is '>' or '<' or '=' or '^' or '~' or ' '))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }
}

/// <summary>
/// 把 .mrpack 装成一个**新实例**：读依赖 → 确保原版资源 → 装加载器 → 建实例 → 导入 files/overrides。
/// 本地 zip 与 Modrinth 下载的 .mrpack 走同一个入口。
/// </summary>
public sealed class ModpackInstaller(
    DataLayout layout,
    InstanceStore instances,
    InstancePackService packs,
    LoaderProvisioner provisioner,
    VersionFetcher fetcher,
    IRemoteFileClient client)
{
    public async Task<InstanceConfig> InstallAsync(
        string mrpackPath,
        string instanceName,
        int memoryMb,
        CancellationToken cancellationToken)
    {
        string indexJson;
        using (var archive = ZipFile.OpenRead(mrpackPath))
        {
            var entry = archive.GetEntry("modrinth.index.json")
                ?? throw new InvalidDataException("不是 .mrpack：缺少 modrinth.index.json");
            using var reader = new StreamReader(entry.Open());
            indexJson = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        var dependencies = ModpackIndex.ParseDependencies(indexJson);
        Console.WriteLine($"整合包依赖：MC {dependencies.MinecraftVersion}, 加载器 {dependencies.LoaderKey ?? "vanilla"} {dependencies.LoaderVersion}");

        await fetcher.FetchAsync(dependencies.MinecraftVersion, new FetchOptions(), null, cancellationToken)
            .ConfigureAwait(false);

        var versionId = dependencies.MinecraftVersion;
        if (dependencies.LoaderKey is not null)
        {
            var kind = dependencies.LoaderKey switch
            {
                "fabric-loader" => LoaderKind.Fabric,
                "quilt-loader" => LoaderKind.Quilt,
                "forge" => LoaderKind.Forge,
                "neoforge" => LoaderKind.NeoForge,
                _ => throw new InvalidOperationException($"未知加载器：{dependencies.LoaderKey}"),
            };

            JavaRuntime? java = null;
            if (kind is LoaderKind.Forge or LoaderKind.NeoForge)
            {
                var selection = JavaSelector.Select(
                    dependencies.MinecraftVersion.StartsWith("1.18", StringComparison.Ordinal) ? 17 : 17,
                    JavaDiscovery.Discover([layout.JavaDir]),
                    null);
                java = selection.Runtime;
            }

            versionId = await provisioner
                .InstallAsync(kind, dependencies.MinecraftVersion, dependencies.LoaderVersion, java, cancellationToken)
                .ConfigureAwait(false);
            await fetcher
                .FetchAsync(versionId, new FetchOptions { Selection = new FetchSelection(Client: false, Libraries: true, Assets: false, Natives: true) }, null, cancellationToken)
                .ConfigureAwait(false);
        }

        var instance = instances.Create(instanceName, versionId, memoryMb, username: null);
        var (downloaded, overridden, skipped) = await packs
            .ImportAsync(mrpackPath, instance.Name, client, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine($"整合包入库：下载 {downloaded}，覆盖 {overridden}，跳过 {skipped}");
        return instance;
    }
}
