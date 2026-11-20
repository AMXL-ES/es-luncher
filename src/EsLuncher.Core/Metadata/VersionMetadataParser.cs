using System.Text.Json;

namespace EsLuncher.Core.Metadata;

/// <summary>
/// 解析单个 version.json。实测 26.3（新格式，45 KB）与 1.7.10（旧格式，14 KB）都能吃下。
/// 解析失败一律抛 <see cref="MetadataFormatException"/>，并带上 <c>$.libraries[3].name</c> 级路径。
/// </summary>
public static class VersionMetadataParser
{
    public static VersionMetadata Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static VersionMetadata Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException("$", $"根节点应为 object，实际 {root.ValueKind}");
        }

        return new VersionMetadata
        {
            Id = JsonRead.RequiredString(root, "id", "$"),
            Type = JsonRead.RequiredString(root, "type", "$"),
            MainClass = JsonRead.RequiredString(root, "mainClass", "$"),
            InheritsFrom = JsonRead.OptionalString(root, "inheritsFrom", "$"),
            Assets = JsonRead.OptionalString(root, "assets", "$"),
            AssetIndex = ParseAssetIndex(root),
            JavaVersion = ParseJavaVersion(root),
            LegacyMinecraftArguments = JsonRead.OptionalString(root, "minecraftArguments", "$"),
            Arguments = ParseArguments(root),
            Libraries = ParseLibraries(root),
            Client = ParseDownload(root, "client"),
            Server = ParseDownload(root, "server"),
            Logging = ParseLogging(root),
            MinimumLauncherVersion = JsonRead.OptionalInt32(root, "minimumLauncherVersion", "$"),
            ComplianceLevel = JsonRead.OptionalInt32(root, "complianceLevel", "$"),
            ReleaseTime = JsonRead.OptionalDateTimeOffset(root, "releaseTime", "$"),
        };
    }

    internal static DownloadArtifact ParseArtifact(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new MetadataFormatException(path, $"应为 object，实际 {element.ValueKind}");
        }

        return new DownloadArtifact(
            Url: JsonRead.RequiredString(element, "url", path),
            Size: JsonRead.OptionalInt64(element, "size", path),
            Sha1: JsonRead.OptionalString(element, "sha1", path))
        {
            Path = JsonRead.OptionalString(element, "path", path),
        };
    }

    /// <summary>解析 <c>rules</c>；供 libraries 与 arguments 共用。</summary>
    internal static IReadOnlyList<Rule> ParseRules(JsonElement parent, string path)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty("rules", out var rulesElement))
        {
            return [];
        }

        if (rulesElement.ValueKind != JsonValueKind.Array)
        {
            throw new MetadataFormatException($"{path}.rules", $"应为数组，实际 {rulesElement.ValueKind}");
        }

        var rules = new List<Rule>();
        var index = 0;
        foreach (var item in rulesElement.EnumerateArray())
        {
            var rulePath = $"{path}.rules[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new MetadataFormatException(rulePath, $"应为 object，实际 {item.ValueKind}");
            }

            var actionText = JsonRead.RequiredString(item, "action", rulePath);
            var action = actionText switch
            {
                "allow" => RuleAction.Allow,
                "disallow" => RuleAction.Disallow,
                _ => throw new MetadataFormatException($"{rulePath}.action", $"未知 action '{actionText}'（只接受 allow/disallow）"),
            };

            OsConstraint? os = null;
            if (JsonRead.TryGetObject(item, "os", out var osElement))
            {
                os = OsConstraint.Create(
                    JsonRead.OptionalString(osElement, "name", $"{rulePath}.os"),
                    JsonRead.OptionalString(osElement, "arch", $"{rulePath}.os"),
                    JsonRead.OptionalString(osElement, "version", $"{rulePath}.os"));
            }

            IReadOnlyDictionary<string, bool> features = Rule.NoFeatures;
            if (JsonRead.TryGetObject(item, "features", out var featuresElement))
            {
                var map = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var property in featuresElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        throw new MetadataFormatException($"{rulePath}.features.{property.Name}", "feature 值必须是布尔");
                    }

                    map[property.Name] = property.Value.GetBoolean();
                }

                features = map;
            }

            rules.Add(new Rule { Action = action, Os = os, Features = features });
        }

        return rules;
    }

    private static IReadOnlyList<Library> ParseLibraries(JsonElement root)
    {
        var librariesElement = JsonRead.RequiredArray(root, "libraries", "$");
        var libraries = new List<Library>(librariesElement.GetArrayLength());
        var index = 0;
        foreach (var item in librariesElement.EnumerateArray())
        {
            var path = $"$.libraries[{index++}]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new MetadataFormatException(path, $"应为 object，实际 {item.ValueKind}");
            }

            DownloadArtifact? artifact = null;
            IReadOnlyDictionary<string, DownloadArtifact> classifiers = Library.NoClassifiers;
            if (JsonRead.TryGetObject(item, "downloads", out var downloads))
            {
                if (downloads.TryGetProperty("artifact", out var artifactElement))
                {
                    artifact = ParseArtifact(artifactElement, $"{path}.downloads.artifact");
                }

                if (JsonRead.TryGetObject(downloads, "classifiers", out var classifiersElement))
                {
                    var map = new Dictionary<string, DownloadArtifact>(StringComparer.Ordinal);
                    foreach (var property in classifiersElement.EnumerateObject())
                    {
                        map[property.Name] = ParseArtifact(property.Value, $"{path}.downloads.classifiers.{property.Name}");
                    }

                    classifiers = map;
                }
            }

            IReadOnlyDictionary<string, string> natives = Library.NoNatives;
            if (JsonRead.TryGetObject(item, "natives", out var nativesElement))
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in nativesElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String)
                    {
                        throw new MetadataFormatException($"{path}.natives.{property.Name}", "natives 值必须是字符串");
                    }

                    map[property.Name] = property.Value.GetString() ?? string.Empty;
                }

                natives = map;
            }

            IReadOnlyList<string> extractExclude = [];
            if (JsonRead.TryGetObject(item, "extract", out var extractElement)
                && extractElement.TryGetProperty("exclude", out var excludeElement))
            {
                if (excludeElement.ValueKind != JsonValueKind.Array)
                {
                    throw new MetadataFormatException($"{path}.extract.exclude", "应为字符串数组");
                }

                var list = new List<string>();
                var excludeIndex = 0;
                foreach (var entry in excludeElement.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String)
                    {
                        throw new MetadataFormatException($"{path}.extract.exclude[{excludeIndex++}]", "应为字符串");
                    }

                    list.Add(entry.GetString() ?? string.Empty);
                }

                extractExclude = list;
            }

            libraries.Add(new Library
            {
                Name = JsonRead.RequiredString(item, "name", path),
                Artifact = artifact,
                Classifiers = classifiers,
                NativeClassifiers = natives,
                Rules = ParseRules(item, path),
                ExtractExclude = extractExclude,
                BaseUrl = JsonRead.OptionalString(item, "url", path),
            });
        }

        return libraries;
    }

    private static Arguments? ParseArguments(JsonElement root)
    {
        if (!JsonRead.TryGetObject(root, "arguments", out var argumentsElement))
        {
            return null;
        }

        return new Arguments(
            ParseArgumentEntries(argumentsElement, "game"),
            ParseArgumentEntries(argumentsElement, "jvm"));
    }

    private static IReadOnlyList<ArgumentEntry> ParseArgumentEntries(JsonElement parent, string key)
    {
        if (!parent.TryGetProperty(key, out var arrayElement))
        {
            return [];
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            throw new MetadataFormatException($"$.arguments.{key}", $"应为数组，实际 {arrayElement.ValueKind}");
        }

        var entries = new List<ArgumentEntry>();
        var index = 0;
        foreach (var item in arrayElement.EnumerateArray())
        {
            var path = $"$.arguments.{key}[{index++}]";
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    entries.Add(new ArgumentEntry([], [item.GetString() ?? string.Empty]));
                    continue;

                case JsonValueKind.Object:
                    entries.Add(ParseConditionalArgument(item, path));
                    continue;

                default:
                    throw new MetadataFormatException(path, $"应为字符串或对象，实际 {item.ValueKind}");
            }
        }

        return entries;
    }

    private static ArgumentEntry ParseConditionalArgument(JsonElement item, string path)
    {
        var rules = ParseRules(item, path);

        if (!item.TryGetProperty("value", out var valueElement))
        {
            throw new MetadataFormatException($"{path}.value", "缺少 value");
        }

        IReadOnlyList<string> values = valueElement.ValueKind switch
        {
            JsonValueKind.String => [valueElement.GetString() ?? string.Empty],
            JsonValueKind.Array => ParseStringArray(valueElement, $"{path}.value"),
            _ => throw new MetadataFormatException($"{path}.value", $"应为字符串或字符串数组，实际 {valueElement.ValueKind}"),
        };

        return new ArgumentEntry(rules, values);
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement arrayElement, string path)
    {
        var list = new List<string>(arrayElement.GetArrayLength());
        var index = 0;
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new MetadataFormatException($"{path}[{index++}]", "应为字符串");
            }

            list.Add(item.GetString() ?? string.Empty);
        }

        return list;
    }

    private static AssetIndex? ParseAssetIndex(JsonElement root)
    {
        if (!JsonRead.TryGetObject(root, "assetIndex", out var element))
        {
            return null;
        }

        return new AssetIndex
        {
            Id = JsonRead.OptionalString(element, "id", "$.assetIndex"),
            Url = JsonRead.RequiredString(element, "url", "$.assetIndex"),
            Sha1 = JsonRead.RequiredString(element, "sha1", "$.assetIndex"),
            Size = JsonRead.OptionalInt64(element, "size", "$.assetIndex"),
            TotalSize = JsonRead.OptionalInt64(element, "totalSize", "$.assetIndex"),
        };
    }

    private static JavaVersionInfo? ParseJavaVersion(JsonElement root)
    {
        if (!JsonRead.TryGetObject(root, "javaVersion", out var element))
        {
            return null;
        }

        var major = JsonRead.OptionalInt32(element, "majorVersion", "$.javaVersion")
            ?? throw new MetadataFormatException("$.javaVersion.majorVersion", "缺少 majorVersion");

        return new JavaVersionInfo(JsonRead.OptionalString(element, "component", "$.javaVersion"), major);
    }

    private static LoggingInfo? ParseLogging(JsonElement root)
    {
        if (!JsonRead.TryGetObject(root, "logging", out var logging) || !JsonRead.TryGetObject(logging, "client", out var client))
        {
            return null;
        }

        return new LoggingInfo(
            JsonRead.RequiredString(client, "argument", "$.logging.client"),
            ParseArtifact(JsonRead.RequiredObject(client, "file", "$.logging.client"), "$.logging.client.file"),
            JsonRead.OptionalString(client, "type", "$.logging.client"));
    }

    private static DownloadArtifact? ParseDownload(JsonElement root, string key)
    {
        if (!JsonRead.TryGetObject(root, "downloads", out var downloads)
            || !downloads.TryGetProperty(key, out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParseArtifact(element, $"$.downloads.{key}");
    }
}
