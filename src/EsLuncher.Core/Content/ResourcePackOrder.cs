using System.Text.Json;
using System.Text.Json.Nodes;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

/// <summary>
/// 资源包顺序：改 options.txt 里的 resourcePacks JSON 数组（把条目上下移动）。
/// 只改这一个键，其余行原样保留；写前留 .bak。
/// </summary>
public sealed class ResourcePackOrder(DataLayout layout, InstanceStore instances)
{
    public IReadOnlyList<string> List(string instanceName)
    {
        var options = ReadOptions(instanceName);
        if (options is null)
        {
            return [];
        }

        var node = ParsePacks(options);
        return node?.AsArray().Select(item => item?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
    }

    /// <summary>把某个资源包上移（-1）或下移（+1）；找不到返回 false。</summary>
    public bool Move(string instanceName, string packId, int delta)
    {
        var path = OptionsPath(instanceName);
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path);
        var node = ParsePacks(text);
        if (node is null)
        {
            return false;
        }

        var array = node.AsArray();
        var index = -1;
        for (var i = 0; i < array.Count; i++)
        {
            if (string.Equals(array[i]?.GetValue<string>(), packId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var target = index + delta;
        if (index < 0 || target < 0 || target >= array.Count)
        {
            return false;
        }

        var sourceValue = array[index]?.GetValue<string>();
        var targetValue = array[target]?.GetValue<string>();
        array[index] = JsonValue.Create(targetValue);
        array[target] = JsonValue.Create(sourceValue);

        var updated = ReplacePacks(text, array.ToJsonString());
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, updated);
        return true;
    }

    private string OptionsPath(string instanceName)
    {
        _ = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        return Path.Combine(layout.GameDir(instanceName), "options.txt");
    }

    private string? ReadOptions(string instanceName)
    {
        var path = OptionsPath(instanceName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static JsonArray? ParsePacks(string optionsText)
    {
        foreach (var line in optionsText.Split('\n'))
        {
            var trimmed = line.Trim();
            var index = trimmed.IndexOf("resourcePacks:", StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var raw = trimmed[(index + "resourcePacks:".Length)..].Trim();
            try
            {
                return JsonNode.Parse(raw)?.AsArray();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private static string ReplacePacks(string optionsText, string jsonArray)
    {
        var lines = optionsText.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("resourcePacks:", StringComparison.Ordinal))
            {
                lines[i] = "resourcePacks:" + jsonArray;
            }
        }

        return string.Join('\n', lines);
    }
}
