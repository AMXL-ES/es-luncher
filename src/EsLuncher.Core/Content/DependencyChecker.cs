using EsLuncher.Core.Content;

namespace EsLuncher.Core.Content;

public sealed record DependencyCheckResult(IReadOnlyList<string> Installed, IReadOnlyList<string> Missing);

/// <summary>
/// 依赖体检：读 mods 目录里每个 jar 的 modId 与 depends，找出"装了但缺前置"的模组。
/// 只做静态检查（不知道版本范围是否满足），足够在崩溃前给人提示。
/// </summary>
public sealed class DependencyChecker(InstanceContentService content)
{
    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft",
        "fabricloader",
        "fabric-loader",
        "java",
        "forge",
        "neoforge",
        "quilt_loader",
        "quilt-loader",
    };

    public DependencyCheckResult Check(string instanceName)
    {
        var directory = content.DirectoryFor(instanceName, ContentKind.Mods);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in content.List(instanceName, ContentKind.Mods))
        {
            var metadata = ModMetadataReader.Read(Path.Combine(directory, mod.Name));
            if (!string.IsNullOrWhiteSpace(metadata.ModId))
            {
                installed.Add(metadata.ModId!);
            }

            foreach (var dependency in metadata.Dependencies)
            {
                required.Add(dependency);
            }
        }

        var missing = required
            .Where(dependency => !installed.Contains(dependency) && !Builtins.Contains(dependency))
            .Where(dependency => !ProvidedByBundle(installed, dependency))
            .OrderBy(dependency => dependency, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DependencyCheckResult(installed.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(), missing);
    }

    /// <summary>
    /// Fabric API / Quilt 的 jar 里是一堆子模块（fabric-api-base、fabric-key-binding-api-v1…），
    /// 静态读取只能看到顶层的 fabric-api / quilt-api；有它就算这些前置已满足。
    /// </summary>
    private static bool ProvidedByBundle(HashSet<string> installed, string dependency)
    {
        if (installed.Contains("fabric-api")
            && (dependency.Equals("fabric", StringComparison.OrdinalIgnoreCase)
                || dependency.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)
                || dependency.StartsWith("fabric_", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (installed.Contains("quilt-api")
            && dependency.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
