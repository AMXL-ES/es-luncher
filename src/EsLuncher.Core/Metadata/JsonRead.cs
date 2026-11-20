using System.Globalization;
using System.Text.Json;

namespace EsLuncher.Core.Metadata;

/// <summary>
/// 带 JSON 路径的严格读取器。所有错误都抛 <see cref="MetadataFormatException"/>，
/// 消息里必须能定位到 <c>$.libraries[3].name</c> 这种粒度。
/// </summary>
internal static class JsonRead
{
    public static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out value))
        {
            return value.ValueKind == JsonValueKind.Object;
        }

        value = default;
        return false;
    }

    public static JsonElement RequiredObject(JsonElement parent, string name, string path)
    {
        if (!TryGetObject(parent, name, out var value))
        {
            throw new MetadataFormatException($"{path}.{name}", "缺少必需的对象字段");
        }

        return value;
    }

    public static JsonElement RequiredArray(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            throw new MetadataFormatException($"{path}.{name}", "缺少必需的数组字段");
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new MetadataFormatException($"{path}.{name}", $"应为数组，实际 {value.ValueKind}");
        }

        return value;
    }

    public static string RequiredString(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            throw new MetadataFormatException($"{path}.{name}", "缺少必需的字符串字段");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new MetadataFormatException($"{path}.{name}", $"应为字符串，实际 {value.ValueKind}");
        }

        return value.GetString() ?? string.Empty;
    }

    public static string? OptionalString(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new MetadataFormatException($"{path}.{name}", $"应为字符串，实际 {value.ValueKind}");
        }

        return value.GetString();
    }

    public static int? OptionalInt32(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new MetadataFormatException($"{path}.{name}", "应为 32 位整数");
        }

        return number;
    }

    public static long? OptionalInt64(JsonElement parent, string name, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw new MetadataFormatException($"{path}.{name}", "应为 64 位整数");
        }

        return number;
    }

    public static DateTimeOffset? OptionalDateTimeOffset(JsonElement parent, string name, string path)
    {
        var text = OptionalString(parent, name, path);
        if (text is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new MetadataFormatException($"{path}.{name}", $"不是合法的 ISO-8601 时间：'{text}'");
        }

        return parsed;
    }
}
