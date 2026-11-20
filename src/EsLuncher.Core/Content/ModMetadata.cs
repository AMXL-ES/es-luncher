using System.IO.Compression;
using System.Text.Json;

namespace EsLuncher.Core.Content;

/// <summary>
/// 从 jar 里读最小可用的模组元数据：Fabric（fabric.mod.json）与 Forge/NeoForge（META-INF/mods.toml）。
/// 只读需要展示的字段，不引入完整 TOML 解析器；解析失败返回 "unknown"，绝不抛给调用方。
/// </summary>
public sealed record ModMetadata(string? ModId, string? Name, string? Version, string Loader, IReadOnlyList<string> Dependencies)
{
    public static ModMetadata Unknown { get; } = new(null, null, null, "unknown", []);
}

public static class ModMetadataReader
{
    public static ModMetadata Read(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var fabric = archive.GetEntry("fabric.mod.json");
            if (fabric is not null)
            {
                return ReadFabric(fabric);
            }

            var forge = archive.GetEntry("META-INF/mods.toml");
            if (forge is not null)
            {
                return ReadForge(forge);
            }

            return ModMetadata.Unknown;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            return ModMetadata.Unknown;
        }
    }

    private static ModMetadata ReadFabric(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var dependencies = new List<string>();
        if (root.TryGetProperty("depends", out var depends) && depends.ValueKind == JsonValueKind.Object)
        {
            foreach (var dependency in depends.EnumerateObject())
            {
                if (dependency.Name is "minecraft" or "fabricloader" or "java" or "fabric-api")
                {
                    continue;
                }

                dependencies.Add(dependency.Name);
            }
        }

        return new ModMetadata(
            GetString(root, "id"),
            GetString(root, "name"),
            GetString(root, "version"),
            "fabric",
            dependencies);
    }

    private static ModMetadata ReadForge(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();

        string? modId = null;
        string? name = null;
        string? version = null;
        var dependencies = new List<string>();
        var inModsBlock = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                inModsBlock = line.StartsWith("[[mods", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.StartsWith("[[dependencies.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = Unquote(line[(equals + 1)..].Trim());

            if (inModsBlock)
            {
                switch (key)
                {
                    case "modId":
                        modId ??= value;
                        break;
                    case "displayName":
                        name ??= value;
                        break;
                    case "version":
                        version ??= value;
                        break;
                }
            }

            if (key == "modId" && !inModsBlock && value != modId && !dependencies.Contains(value))
            {
                dependencies.Add(value);
            }
        }

        return new ModMetadata(modId, name, version, "forge", dependencies);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}
