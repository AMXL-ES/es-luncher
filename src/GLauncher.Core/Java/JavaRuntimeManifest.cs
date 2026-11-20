using System.Text.Json;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Java;

public enum RuntimeEntryType
{
    File,
    Directory,
    Link,
}

/// <param name="Path">相对运行时根目录的路径（正斜杠）。</param>
/// <param name="Url">仅 file 类型有。</param>
public sealed record RuntimeFile(
    string Path,
    RuntimeEntryType Type,
    string? Url,
    string? Sha1,
    long? Size,
    bool Executable);

/// <summary>
/// 解析某个组件的 runtime manifest（如 jre-legacy 的 80 KB manifest.json）。
/// 实测：208 条目 = 192 file + 16 directory，raw 合计 143.2 MiB。
/// </summary>
public static class JavaRuntimeManifestParser
{
    public static IReadOnlyList<RuntimeFile> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$.files", "运行时 manifest 缺少 files");
        }

        var result = new List<RuntimeFile>();
        foreach (var property in files.EnumerateObject())
        {
            var path = property.Name;
            var entry = property.Value;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new MetadataFormatException($"$.files.{path}", "应为 object");
            }

            var typeText = JsonRead.RequiredString(entry, "type", $"$.files.{path}");
            var type = typeText switch
            {
                "file" => RuntimeEntryType.File,
                "directory" => RuntimeEntryType.Directory,
                "link" => RuntimeEntryType.Link,
                _ => throw new MetadataFormatException($"$.files.{path}.type", $"未知类型 '{typeText}'"),
            };

            if (!IsSafeRelativePath(path))
            {
                throw new MetadataFormatException($"$.files.{path}", "运行时文件路径必须是不含 .. 的相对路径");
            }

            string? url = null;
            string? sha1 = null;
            long? size = null;
            if (type == RuntimeEntryType.File)
            {
                var downloads = JsonRead.RequiredObject(entry, "downloads", $"$.files.{path}");
                var raw = JsonRead.RequiredObject(downloads, "raw", $"$.files.{path}.downloads");
                url = JsonRead.RequiredString(raw, "url", $"$.files.{path}.downloads.raw");
                sha1 = JsonRead.RequiredString(raw, "sha1", $"$.files.{path}.downloads.raw");
                size = JsonRead.OptionalInt64(raw, "size", $"$.files.{path}.downloads.raw");
            }

            var executable = entry.TryGetProperty("executable", out var exeElement)
                && exeElement.ValueKind == JsonValueKind.True;

            result.Add(new RuntimeFile(path, type, url, sha1, size, executable));
        }

        return result;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':'))
        {
            return false;
        }

        return !path.Split('/').Any(segment => segment is "." or ".." or "");
    }
}
