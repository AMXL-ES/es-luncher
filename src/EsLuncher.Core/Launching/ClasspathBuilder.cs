using EsLuncher.Core.Downloads;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Launching;

/// <summary>
/// classpath 组装：按 version.json 里 libraries 的顺序（原版即为官方顺序），
/// 只放**规则允许且带 artifact** 的库；旧版 natives 是解压用的，**不进 classpath**；
/// 客户端 jar 放最后。
/// </summary>
public static class ClasspathBuilder
{
    public static IReadOnlyList<string> Build(VersionMetadata version, LaunchEnvironment environment, DataLayout layout)
    {
        var entries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var library in version.Libraries)
        {
            if (!library.IsAllowed(environment) || library.Artifact is null)
            {
                continue;
            }

            var relative = library.Artifact.Path ?? MavenCoordinate.Parse(library.Name).ToMavenPath();
            var full = Path.Combine(layout.LibrariesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (seen.Add(full))
            {
                entries.Add(full);
            }
        }

        var clientJar = layout.ClientJar(version.Id);
        if (seen.Add(clientJar))
        {
            entries.Add(clientJar);
        }

        return entries;
    }
}
