using System.Text;
using System.Text.RegularExpressions;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Launching;

/// <summary>
/// 组装启动参数（本里程碑只做字符串层面：规则过滤 + 占位符展开 + 旧格式分词）。
/// classpath 排序、natives 路径、内存参数等属于 M2，不在这里做。
/// </summary>
public static class ArgumentAssembler
{
    private static readonly Regex PlaceholderPattern =
        new(@"\$\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>game 侧参数：新格式按 rules 过滤，旧格式走 <c>minecraftArguments</c> 模板。</summary>
    public static IReadOnlyList<string> BuildGameArguments(
        VersionMetadata version,
        LaunchEnvironment environment,
        IReadOnlyDictionary<string, string> values)
    {
        if (version.Arguments is not null)
        {
            return BuildFromEntries(version.Arguments.Game, environment, values);
        }

        if (!string.IsNullOrWhiteSpace(version.LegacyMinecraftArguments))
        {
            return BuildFromLegacyTemplate(version.LegacyMinecraftArguments, values);
        }

        throw new MetadataFormatException("$", "版本既没有 arguments 也没有 minecraftArguments，无法组装启动参数");
    }

    /// <summary>jvm 侧参数：只有新格式有；旧格式由启动器自己拼。</summary>
    public static IReadOnlyList<string> BuildJvmArguments(
        VersionMetadata version,
        LaunchEnvironment environment,
        IReadOnlyDictionary<string, string> values)
    {
        return version.Arguments is null
            ? []
            : BuildFromEntries(version.Arguments.Jvm, environment, values);
    }

    /// <summary>展开一个字符串模板里的全部 <c>${name}</c>；缺值直接抛，不静默留空。</summary>
    public static string ExpandTemplate(string template, IReadOnlyDictionary<string, string> values)
        => Expand(template, values, "$");

    private static IReadOnlyList<string> BuildFromEntries(
        IReadOnlyList<ArgumentEntry> entries,
        LaunchEnvironment environment,
        IReadOnlyDictionary<string, string> values)
    {
        var arguments = new List<string>();
        foreach (var entry in entries)
        {
            if (!entry.AppliesTo(environment))
            {
                continue;
            }

            foreach (var value in entry.Values)
            {
                arguments.Add(Expand(value, values, "$.arguments"));
            }
        }

        return arguments;
    }

    private static IReadOnlyList<string> BuildFromLegacyTemplate(
        string template,
        IReadOnlyDictionary<string, string> values)
    {
        // 先分词再展开：占位符的值可能含空格（例如 Windows 用户目录），
        // 展开后不能再被空格切开。
        var arguments = new List<string>();
        foreach (var token in Tokenize(template))
        {
            arguments.Add(Expand(token, values, "$.minecraftArguments"));
        }

        return arguments;
    }

    /// <summary>
    /// 按空白分词，支持双引号包裹（比 Mojang 原版更宽容；原版遇到含空格路径会切坏）。
    /// 引号未闭合视为格式错误。
    /// </summary>
    internal static IReadOnlyList<string> Tokenize(string template)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var ch in template)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(ch);
            hasToken = true;
        }

        if (inQuotes)
        {
            throw new MetadataFormatException("$.minecraftArguments", "引号未闭合");
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static string Expand(string template, IReadOnlyDictionary<string, string> values, string context)
    {
        return PlaceholderPattern.Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value)
                ? value
                : throw new LaunchPlaceholderException(name, context);
        });
    }
}
