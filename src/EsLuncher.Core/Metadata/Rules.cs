using System.Text.RegularExpressions;

namespace EsLuncher.Core.Metadata;

public enum RuleAction
{
    Allow,
    Disallow,
}

/// <summary>
/// <c>rules[].os</c>。三者都可缺省，缺省表示"不限"。
/// <c>version</c> 是正则（Mojang 用它匹配 <c>^10\.</c> 这类系统版本）。
/// </summary>
public sealed record OsConstraint
{
    public string? Name { get; init; }

    public string? Arch { get; init; }

    public string? VersionPattern { get; init; }

    internal Regex? VersionRegex { get; init; }

    public static OsConstraint Create(string? name, string? arch, string? versionPattern)
    {
        Regex? regex = null;
        if (!string.IsNullOrEmpty(versionPattern))
        {
            try
            {
                regex = new Regex(versionPattern, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new MetadataFormatException("$.rules[].os.version", $"非法正则 '{versionPattern}'：{ex.Message}");
            }
        }

        return new OsConstraint
        {
            Name = name,
            Arch = arch,
            VersionPattern = versionPattern,
            VersionRegex = regex,
        };
    }

    public bool Matches(LaunchEnvironment environment)
    {
        if (Name is not null && !string.Equals(Name, environment.OsName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Arch is not null && !string.Equals(Arch, environment.OsArch, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return VersionRegex is null || VersionRegex.IsMatch(environment.OsVersion);
    }
}

/// <summary>
/// 一条规则。缺省字段表示"不限"；<c>features</c> 里的键必须全部与当前环境一致。
/// </summary>
public sealed record Rule
{
    public static readonly IReadOnlyDictionary<string, bool> NoFeatures = new Dictionary<string, bool>(0);

    public required RuleAction Action { get; init; }

    public OsConstraint? Os { get; init; }

    public IReadOnlyDictionary<string, bool> Features { get; init; } = NoFeatures;

    public bool AppliesTo(LaunchEnvironment environment)
    {
        if (Os is not null && !Os.Matches(environment))
        {
            return false;
        }

        foreach (var (feature, expected) in Features)
        {
            if (!environment.Features.TryGetValue(feature, out var actual) || actual != expected)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 规则求值：**没有规则 = 允许**；否则从"不允许"开始，逐条命中就按 action 覆盖（后者胜）。
/// 这个语义同时满足两种真实数据：
/// 26.3 的 <c>[{"allow","os":{"name":"osx"}}]</c>（不命中即排除），
/// 1.12.2 的 <c>[{"allow"},{"disallow","os":{"name":"osx"}}]</c>（先全允许再排除 osx）。
/// </summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(IReadOnlyList<Rule> rules, LaunchEnvironment environment)
    {
        if (rules.Count == 0)
        {
            return true;
        }

        var allowed = false;
        foreach (var rule in rules)
        {
            if (rule.AppliesTo(environment))
            {
                allowed = rule.Action == RuleAction.Allow;
            }
        }

        return allowed;
    }
}
