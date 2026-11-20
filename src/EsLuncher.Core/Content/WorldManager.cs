using System.IO.Compression;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

public sealed record WorldInfo(string Name, string Path, long SizeBytes, DateTimeOffset Modified);

/// <summary>
/// 世界管理：列出、大小统计、重命名、备份为 zip、从 zip 还原。
/// 还原只解压到 saves/&lt;名字&gt;，同名默认拒绝，避免覆盖存档。
/// </summary>
public sealed class WorldManager(DataLayout layout, InstanceStore instances)
{
    public string SavesDirectory(string instanceName)
    {
        _ = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        return Path.Combine(layout.GameDir(instanceName), "saves");
    }

    public IReadOnlyList<WorldInfo> List(string instanceName)
    {
        var saves = SavesDirectory(instanceName);
        if (!Directory.Exists(saves))
        {
            return [];
        }

        return Directory.EnumerateDirectories(saves)
            .Select(path => new WorldInfo(
                Path.GetFileName(path),
                path,
                DirectorySize(path),
                Directory.GetLastWriteTimeUtc(path)))
            .OrderBy(world => world.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }
        }

        return total;
    }

    public void Rename(string instanceName, string oldName, string newName)
    {
        ValidateName(newName);
        var saves = SavesDirectory(instanceName);
        var source = Path.Combine(saves, oldName);
        var target = Path.Combine(saves, newName);
        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException($"世界不存在：{oldName}");
        }

        if (Directory.Exists(target))
        {
            throw new InvalidOperationException($"目标世界已存在：{newName}");
        }

        Directory.Move(source, target);
    }

    public string Backup(string instanceName, string worldName, string outZip)
    {
        var source = Path.Combine(SavesDirectory(instanceName), worldName);
        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException($"世界不存在：{worldName}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outZip));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outZip))
        {
            File.Delete(outZip);
        }

        ZipFile.CreateFromDirectory(source, outZip);
        return outZip;
    }

    public string Restore(string instanceName, string zipPath, string worldName)
    {
        ValidateName(worldName);
        var saves = SavesDirectory(instanceName);
        Directory.CreateDirectory(saves);
        var target = Path.Combine(saves, worldName);
        if (Directory.Exists(target))
        {
            throw new InvalidOperationException($"世界已存在：{worldName}（先重命名或手动移走）");
        }

        Directory.CreateDirectory(target);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, target);
            return target;
        }
        catch
        {
            try
            {
                Directory.Delete(target, recursive: true);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name.Contains('/') || name.Contains('\\')
            || name is "." or "..")
        {
            throw new ArgumentException($"世界名不合法：'{name}'");
        }
    }
}
