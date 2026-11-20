using System.IO.Compression;

namespace EsLuncher.Core.Downloads;

/// <summary>
/// 解压旧版 natives jar（≤1.12.2）。必须防 zip slip：任何逃出目标目录的条目直接判整包失败。
/// </summary>
public static class NativesExtractor
{
    public static int Extract(string jarPath, string targetDirectory, IReadOnlyList<string> excludePrefixes)
    {
        ArgumentException.ThrowIfNullOrEmpty(jarPath);

        var targetRoot = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetRoot);
        var targetRootWithSeparator = targetRoot.EndsWith(Path.DirectorySeparatorChar)
            ? targetRoot
            : targetRoot + Path.DirectorySeparatorChar;

        var extracted = 0;
        using var archive = ZipFile.OpenRead(jarPath);
        foreach (var entry in archive.Entries)
        {
            // 目录条目的 Name 为空
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var normalizedName = entry.FullName.Replace('\\', '/');
            if (IsExcluded(normalizedName, excludePrefixes))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetRoot, entry.FullName));
            if (!destination.StartsWith(targetRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"natives 包内路径越界（zip slip）：'{entry.FullName}'");
            }

            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destination, overwrite: true);
            extracted++;
        }

        return extracted;
    }

    private static bool IsExcluded(string normalizedName, IReadOnlyList<string> excludePrefixes)
    {
        foreach (var prefix in excludePrefixes)
        {
            if (normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
