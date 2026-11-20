using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

/// <summary>
/// 简版 `.mrpack`：导出时把本地 mods 放进 overrides/（不编造下载地址），
/// 导入时支持 downloads 列表与 overrides/。只处理能确定的路径，越界直接拒绝。
/// </summary>
public sealed class InstancePackService(DataLayout layout, InstanceStore instances, InstanceContentService content)
{
    public async Task ExportAsync(string instanceName, string outZip, CancellationToken cancellationToken)
    {
        var instance = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        var (minecraftVersion, loader) = InstanceVersionInfo.Derive(instance.VersionId);
        var mods = content.List(instanceName, ContentKind.Mods);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outZip));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outZip))
        {
            File.Delete(outZip);
        }

        using var archive = ZipFile.Open(outZip, ZipArchiveMode.Create);
        var dependencies = new JsonObject
        {
            ["minecraft"] = minecraftVersion,
        };
        if (loader is not null)
        {
            dependencies[loader] = "*";
        }

        var index = new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["versionId"] = $"{instance.Name}-1",
            ["name"] = instance.Name,
            ["summary"] = string.Empty,
            ["files"] = new JsonArray(),
            ["dependencies"] = dependencies,
        };

        var indexEntry = archive.CreateEntry("modrinth.index.json");
        await using (var stream = indexEntry.Open())
        {
            await JsonSerializer.SerializeAsync(stream, index, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var modsRoot = content.DirectoryFor(instanceName, ContentKind.Mods);
        foreach (var mod in mods)
        {
            var entry = archive.CreateEntry($"overrides/mods/{mod.Name}");
            await using var source = File.OpenRead(Path.Combine(modsRoot, mod.Name));
            await using var target = entry.Open();
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>把 mrpack 导入**已有实例**：下载 files[] 与复制 overrides/*；返回 (下载, 覆盖, 跳过)。</summary>
    public async Task<(int Downloaded, int Overridden, int Skipped)> ImportAsync(
        string zipPath,
        string instanceName,
        IRemoteFileClient client,
        CancellationToken cancellationToken)
    {
        var instance = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        var gameDirectory = layout.GameDir(instance.Name);

        using var archive = ZipFile.OpenRead(zipPath);
        var indexEntry = archive.GetEntry("modrinth.index.json")
            ?? throw new InvalidDataException("不是 .mrpack：缺少 modrinth.index.json");
        using var reader = new StreamReader(indexEntry.Open());
        var index = JsonNode.Parse(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("modrinth.index.json 解析失败");

        var downloaded = 0;
        var overridden = 0;
        var skipped = 0;

        foreach (var file in index["files"]?.AsArray() ?? [])
        {
            var path = file?["path"]?.GetValue<string>();
            var urls = file?["downloads"]?.AsArray();
            if (string.IsNullOrWhiteSpace(path) || urls is null || urls.Count == 0)
            {
                skipped++;
                continue;
            }

            var target = ResolveTarget(gameDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var uri = new Uri(urls[0]!.GetValue<string>());
            using var response = await client.GetAsync(uri, 0, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess || response.Content is null)
            {
                skipped++;
                continue;
            }

            await using var stream = File.Create(target + ".part");
            await response.Content.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
            File.Move(target + ".part", target, overwrite: true);
            downloaded++;
        }

        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("overrides/", StringComparison.Ordinal)
                || entry.FullName.Length == "overrides/".Length)
            {
                continue;
            }

            var relative = entry.FullName["overrides/".Length..];
            var target = ResolveTarget(gameDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            overridden++;
        }

        return (downloaded, overridden, skipped);
    }

    public static string ResolveTarget(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"包内路径越界：'{relative}'");
        }

        return target;
    }
}

/// <summary>从 "fabric-loader-0.20.1-1.18.2" / "1.18.2-forge-40.3.0" 这类 id 推出 MC 版本与加载器。</summary>
public static class InstanceVersionInfo
{
    public static (string MinecraftVersion, string? Loader) Derive(string versionId)
    {
        if (versionId.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = versionId.Split('-');
            return (parts[^1], "fabric-loader");
        }

        if (versionId.StartsWith("quilt-loader-", StringComparison.OrdinalIgnoreCase))
        {
            var parts = versionId.Split('-');
            return (parts[^1], "quilt-loader");
        }

        var forge = versionId.IndexOf("-forge-", StringComparison.OrdinalIgnoreCase);
        if (forge > 0)
        {
            return (versionId[..forge], "forge");
        }

        var neoforge = versionId.IndexOf("-neoforge-", StringComparison.OrdinalIgnoreCase);
        if (neoforge > 0)
        {
            return (versionId[..neoforge], "neoforge");
        }

        return (versionId, null);
    }
}
