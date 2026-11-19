using GLauncher.Core.Metadata;

namespace GLauncher.Core.Downloads;

/// <summary>
/// 版本清单 + version.json 的获取与缓存。清单没有官方哈希，version.json 有 sha1 必须校验。
/// </summary>
public sealed class VersionCatalog(DataLayout layout, FileDownloader downloader)
{
    public static readonly Uri OfficialManifest =
        new("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");

    public static readonly Uri MirrorManifest =
        new("https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json");

    public async Task<VersionManifest> GetManifestAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (refresh && File.Exists(layout.ManifestFile))
        {
            File.Delete(layout.ManifestFile);
        }

        var job = new DownloadJob
        {
            Primary = OfficialManifest,
            Mirrors = [MirrorManifest],
            TargetPath = layout.ManifestFile,
        };

        var result = await downloader.DownloadAsync(job, cancellationToken).ConfigureAwait(false);
        if (result.Status == FileDownloadStatus.Failed)
        {
            throw new HttpRequestException($"获取版本清单失败：{result.Error}");
        }

        var json = await File.ReadAllTextAsync(layout.ManifestFile, cancellationToken).ConfigureAwait(false);
        return VersionManifestParser.Parse(json);
    }

    public async Task<VersionMetadata> GetVersionAsync(
        string versionId,
        VersionManifest manifest,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var entry = manifest.Versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"版本 '{versionId}' 不在 manifest 中（共 {manifest.Versions.Count} 个版本）");

        var path = layout.VersionJson(versionId);
        if (refresh && File.Exists(path))
        {
            File.Delete(path);
        }

        var job = new DownloadJob
        {
            Primary = new Uri(entry.Url),
            TargetPath = path,
            ExpectedSha1 = entry.Sha1,
        };

        var result = await downloader.DownloadAsync(job, cancellationToken).ConfigureAwait(false);
        if (result.Status == FileDownloadStatus.Failed)
        {
            throw new HttpRequestException($"获取 {versionId} 的 version.json 失败：{result.Error}");
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return VersionMetadataParser.Parse(json);
    }
}
