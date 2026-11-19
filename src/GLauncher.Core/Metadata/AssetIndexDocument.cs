using System.Text.Json;

namespace GLauncher.Core.Metadata;

/// <param name="Hash">对象哈希，同时是落盘文件名。</param>
public sealed record AssetObject(string Hash, long Size);

public sealed record AssetIndexDocument(
    IReadOnlyDictionary<string, AssetObject> Objects,
    bool Virtual,
    bool MapToResources);

/// <summary>
/// 解析 <c>assets/indexes/{id}.json</c>。实测：26.3 有 5,149 个对象 / 465 MiB，键是逻辑路径
/// （如 <c>icons/icon_128x128.png</c>），落盘用 <c>objects/{hash[0..2]}/{hash}</c>。
/// </summary>
public static class AssetIndexParser
{
    public static AssetIndexDocument Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static AssetIndexDocument Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$", $"资源索引根节点应为 object，实际 {root.ValueKind}");
        }

        if (!root.TryGetProperty("objects", out var objectsElement) || objectsElement.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$.objects", "缺少 objects 对象");
        }

        var objects = new Dictionary<string, AssetObject>(StringComparer.Ordinal);
        foreach (var property in objectsElement.EnumerateObject())
        {
            var path = $"$.objects.{property.Name}";
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new MetadataFormatException(path, "应为 object");
            }

            var hash = JsonRead.RequiredString(property.Value, "hash", path);
            if (!Downloads.Sha1Util.IsValidSha1(hash))
            {
                throw new MetadataFormatException($"{path}.hash", $"不是 40 位十六进制哈希：'{hash}'");
            }

            var size = JsonRead.OptionalInt64(property.Value, "size", path)
                ?? throw new MetadataFormatException($"{path}.size", "缺少 size");

            objects[property.Name] = new AssetObject(hash, size);
        }

        return new AssetIndexDocument(
            objects,
            Virtual: OptionalBoolean(root, "virtual"),
            MapToResources: OptionalBoolean(root, "map_to_resources"));
    }

    private static bool OptionalBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new MetadataFormatException($"$.{name}", $"应为布尔，实际 {value.ValueKind}"),
        };
    }
}
