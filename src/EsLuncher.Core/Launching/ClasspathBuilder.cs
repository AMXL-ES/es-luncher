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
            if (!library.IsAllowed(environment))
            {
                continue;
            }

            // 加载器 profile（Fabric/Quilt）常常只给 name + url（maven 基址），没有 downloads.artifact。
            var relative = library.Artifact?.Path
                ?? (library.Artifact is null && library.BaseUrl is null
                    ? null
                    : MavenCoordinate.Parse(library.Name).ToMavenPath());
            if (relative is null)
            {
                continue;
            }

            var full = Path.Combine(layout.LibrariesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (seen.Add(full))
            {
                entries.Add(full);
            }
        }

        var clientJar = layout.ClientJar(version.ClientJarVersionId ?? version.Id);

        // Forge/NeoForge 1.17+ 用 BootstrapLauncher，原版 jar 由 srg/module 方式加载；
        // 再把它塞进 -cp 会与模块系统冲突（module minecraft vs _1_18_2 重复导出包）。
        var usesBootstrapLauncher = string.Equals(
            version.MainClass,
            "cpw.mods.bootstraplauncher.BootstrapLauncher",
            StringComparison.Ordinal);
        if (!usesBootstrapLauncher && seen.Add(clientJar))
        {
            entries.Add(clientJar);
        }

        return entries;
    }
}
