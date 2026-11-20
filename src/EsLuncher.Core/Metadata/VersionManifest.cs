namespace EsLuncher.Core.Metadata;

/// <summary>manifest 的 <c>latest</c> 段。</summary>
public sealed record LatestVersions(string Release, string Snapshot);

/// <summary>manifest 里的一条版本条目。</summary>
/// <param name="Sha1">该 version.json 自身的摘要，不是游戏文件的摘要。</param>
public sealed record VersionEntry(
    string Id,
    string Type,
    string Url,
    string Sha1,
    DateTimeOffset? ReleaseTime,
    int? ComplianceLevel);

/// <summary>
/// <c>version_manifest_v2.json</c>。实测 925 个版本，type 有 release/snapshot/old_beta/old_alpha 四种。
/// </summary>
public sealed record VersionManifest(LatestVersions Latest, IReadOnlyList<VersionEntry> Versions);
