using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Metadata;

/// <summary>版本清单查询（纯函数，便于单测）。</summary>
public static class VersionQuery
{
    public const string AllTypes = "all";

    /// <summary>
    /// 按 manifest 自带顺序（新→旧）过滤 type，取前 <paramref name="count"/> 条。
    /// type 用 <c>all</c> 表示不过滤。
    /// </summary>
    public static IReadOnlyList<VersionEntry> Latest(VersionManifest manifest, string type, int count)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count 至少为 1");
        }

        IEnumerable<VersionEntry> query = manifest.Versions;
        if (!string.Equals(type, AllTypes, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(entry => string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        return query.Take(count).ToArray();
    }

    public static VersionEntry? Find(VersionManifest manifest, string id)
        => manifest.Versions.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
}
