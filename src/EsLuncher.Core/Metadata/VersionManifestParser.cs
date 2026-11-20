using System.Text.Json;

namespace EsLuncher.Core.Metadata;

/// <summary>解析 <c>version_manifest_v2.json</c>。</summary>
public static class VersionManifestParser
{
    public static VersionManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static VersionManifest Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$", $"根节点应为 object，实际 {root.ValueKind}");
        }

        var latestElement = JsonRead.RequiredObject(root, "latest", "$");
        var latest = new LatestVersions(
            JsonRead.RequiredString(latestElement, "release", "$.latest"),
            JsonRead.RequiredString(latestElement, "snapshot", "$.latest"));

        var versionsElement = JsonRead.RequiredArray(root, "versions", "$");
        var versions = new List<VersionEntry>(versionsElement.GetArrayLength());
        var index = 0;
        foreach (var item in versionsElement.EnumerateArray())
        {
            var path = $"$.versions[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new MetadataFormatException(path, $"应为 object，实际 {item.ValueKind}");
            }

            versions.Add(new VersionEntry(
                Id: JsonRead.RequiredString(item, "id", path),
                Type: JsonRead.RequiredString(item, "type", path),
                Url: JsonRead.RequiredString(item, "url", path),
                Sha1: JsonRead.RequiredString(item, "sha1", path),
                ReleaseTime: JsonRead.OptionalDateTimeOffset(item, "releaseTime", path),
                ComplianceLevel: JsonRead.OptionalInt32(item, "complianceLevel", path)));
        }

        return new VersionManifest(latest, versions);
    }
}
