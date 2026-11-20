using System.Text;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

public sealed record ModStatus(
    string FileName,
    string? ModId,
    string LocalVersion,
    string? LatestVersion,
    string? ProjectSlug)
{
    public bool UpdateAvailable => LatestVersion is not null && !Normalize(LocalVersion).Equals(Normalize(LatestVersion), StringComparison.OrdinalIgnoreCase);

    internal static string Normalize(string? version)
    {
        var text = (version ?? string.Empty).Trim();

        // 去掉 "1.18.2-" 这类 MC 版本前缀（mod 元数据常见写法）
        var dash = text.IndexOf('-');
        if (dash > 0 && text[..dash].All(ch => char.IsDigit(ch) || ch == '.'))
        {
            text = text[(dash + 1)..];
        }

        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}

/// <summary>
/// 模组更新检测：用 jar 里的 modId 去 Modrinth 找同名项目，取最新版本号与本地比较。
/// 只做启发式（版本号不保证严格递增），发现"看起来不同"就标记可更新；更新走 Modrinth 安装并回收旧文件。
/// </summary>
public sealed class ModUpdater(InstanceContentService content, ModrinthClient modrinth)
{
    public async Task<IReadOnlyList<ModStatus>> CheckAsync(
        string instanceName,
        string minecraftVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        var directory = content.DirectoryFor(instanceName, ContentKind.Mods);
        var mods = content.List(instanceName, ContentKind.Mods).Where(mod => !mod.IsDirectory).ToArray();
        var statuses = new ModStatus[mods.Length];

        // 并行查（最多 4 个），46 个模组时从十几秒降到几秒。
        using var gate = new SemaphoreSlim(4);
        var tasks = mods.Select(async (mod, index) =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                statuses[index] = await CheckOneAsync(
                        Path.Combine(directory, mod.Name),
                        mod.Name,
                        minecraftVersion,
                        loader,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return statuses;
    }

    private async Task<ModStatus> CheckOneAsync(
        string filePath,
        string fileName,
        string minecraftVersion,
        string? loader,
        CancellationToken cancellationToken)
    {
        var metadata = ModMetadataReader.Read(filePath);
        if (string.IsNullOrWhiteSpace(metadata.ModId))
        {
            return new ModStatus(fileName, null, metadata.Version ?? "?", null, null);
        }

        string? latest = null;
        string? slug = null;
        try
        {
            var hits = await modrinth
                .SearchAsync(metadata.ModId!, minecraftVersion, loader, 5, cancellationToken)
                .ConfigureAwait(false);
            var hit = hits.FirstOrDefault(candidate =>
                string.Equals(candidate.Slug, metadata.ModId, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                slug = hit.Slug;
                var versions = await modrinth
                    .GetVersionsAsync(hit.Slug, minecraftVersion, loader, cancellationToken)
                    .ConfigureAwait(false);
                latest = versions.FirstOrDefault()?.VersionNumber;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // 网络失败就当作"未知"，不阻塞其它模组
        }

        return new ModStatus(fileName, metadata.ModId, metadata.Version ?? "?", latest, slug);
    }

    /// <summary>把所有可更新的模组装到最新版，并回收旧文件；返回更新数量。</summary>
    public async Task<int> UpdateAsync(
        string instanceName,
        string minecraftVersion,
        string? loader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var statuses = await CheckAsync(instanceName, minecraftVersion, loader, cancellationToken).ConfigureAwait(false);
        var updated = 0;

        foreach (var status in statuses.Where(status => status.UpdateAvailable && status.ProjectSlug is not null))
        {
            progress?.Report($"更新 {status.FileName} → {status.LatestVersion} …");
            try
            {
                var file = await modrinth
                    .InstallAsync(content, instanceName, status.ProjectSlug!, minecraftVersion, loader, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(file, status.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    content.Remove(instanceName, ContentKind.Mods, status.FileName);
                }

                updated++;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                progress?.Report($"! {status.FileName} 更新失败：{exception.Message}");
            }
        }

        return updated;
    }

    public static string Describe(ModStatus status)
    {
        var builder = new StringBuilder(status.FileName);
        if (status.ModId is null)
        {
            return builder.Append("（无法识别 modId，跳过）").ToString();
        }

        if (status.LatestVersion is null)
        {
            return builder.Append($"（{status.LocalVersion}，Modrinth 上没有同名项目）").ToString();
        }

        return status.UpdateAvailable
            ? builder.Append($"（{status.LocalVersion} → {status.LatestVersion}，可更新）").ToString()
            : builder.Append($"（{status.LocalVersion}，已是最新）").ToString();
    }
}
