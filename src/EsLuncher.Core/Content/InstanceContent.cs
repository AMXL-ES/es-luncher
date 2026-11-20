using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

public enum ContentKind
{
    Mods,
    ResourcePacks,
    ShaderPacks,
    Worlds,
    Datapacks,
}

/// <param name="Name">展示名（已去掉 .disabled 后缀）。</param>
/// <param name="Enabled">是否启用；世界恒为 true（世界不支持禁用）。</param>
public sealed record ContentFile(string Name, long Size, DateTimeOffset Modified, bool Enabled = true, bool IsDirectory = false);

/// <summary>
/// 实例内容管理：mods / 资源包 / 光影 / 世界 / 数据包。
/// 启用/禁用用 <c>.disabled</c> 后缀（不改内容本身）；删除一律移入回收目录，绝不真删。
/// </summary>
public sealed class InstanceContentService(DataLayout layout, InstanceStore instances)
{
    public const string DisabledSuffix = ".disabled";

    public static string FolderName(ContentKind kind) => kind switch
    {
        ContentKind.Mods => "mods",
        ContentKind.ResourcePacks => "resourcepacks",
        ContentKind.ShaderPacks => "shaderpacks",
        ContentKind.Worlds => "saves",
        ContentKind.Datapacks => "datapacks",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParseKind(string text, out ContentKind kind)
    {
        switch (text.ToLowerInvariant())
        {
            case "mods" or "mod":
                kind = ContentKind.Mods;
                return true;
            case "resourcepacks" or "resourcepack" or "resource-packs":
                kind = ContentKind.ResourcePacks;
                return true;
            case "shaderpacks" or "shaderpack" or "shaders":
                kind = ContentKind.ShaderPacks;
                return true;
            case "worlds" or "world" or "saves":
                kind = ContentKind.Worlds;
                return true;
            case "datapacks" or "datapack":
                kind = ContentKind.Datapacks;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public string DirectoryFor(string instanceName, ContentKind kind, string? world = null)
    {
        EnsureInstance(instanceName);
        var game = layout.GameDir(instanceName);
        return kind switch
        {
            ContentKind.Worlds => Path.Combine(game, "saves"),
            ContentKind.Datapacks => Path.Combine(game, "saves", RequireWorld(world), "datapacks"),
            _ => Path.Combine(game, FolderName(kind)),
        };
    }

    public IReadOnlyList<ContentFile> List(string instanceName, ContentKind kind, string? world = null)
    {
        var directory = DirectoryFor(instanceName, kind, world);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var entries = new List<ContentFile>();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var name = Path.GetFileName(path);
            var enabled = true;
            if (kind != ContentKind.Worlds && name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase))
            {
                enabled = false;
                name = name[..^DisabledSuffix.Length];
            }

            var isDirectory = Directory.Exists(path);
            entries.Add(new ContentFile(
                name,
                isDirectory ? 0 : new FileInfo(path).Length,
                File.GetLastWriteTimeUtc(path),
                enabled,
                isDirectory));
        }

        return entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ContentFile Add(string instanceName, ContentKind kind, string sourcePath, string? world = null)
    {
        var directory = DirectoryFor(instanceName, kind, world);
        if (File.Exists(sourcePath))
        {
            Directory.CreateDirectory(directory);
            var fileName = Path.GetFileName(sourcePath);
            var target = Path.Combine(directory, fileName);
            File.Copy(sourcePath, target, overwrite: true);
            var info = new FileInfo(target);
            return new ContentFile(info.Name, info.Length, info.LastWriteTimeUtc);
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.CreateDirectory(directory);
            var folderName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var target = Path.Combine(directory, folderName);
            CopyDirectory(sourcePath, target);
            return new ContentFile(folderName, 0, Directory.GetLastWriteTimeUtc(target), true, true);
        }

        throw new FileNotFoundException($"源文件不存在：{sourcePath}", sourcePath);
    }

    /// <summary>移除内容：移到 数据根/trash/content/&lt;实例&gt;/&lt;时间戳&gt;-&lt;文件名&gt;，返回是否找到。</summary>
    public bool Remove(string instanceName, ContentKind kind, string name, string? world = null)
    {
        ValidateBareName(name);
        var directory = DirectoryFor(instanceName, kind, world);
        var target = Path.Combine(directory, name);
        if (!Exists(target))
        {
            target += DisabledSuffix;
            if (!Exists(target))
            {
                return false;
            }
        }

        var trashDirectory = Path.Combine(layout.Root, "trash", "content", Sanitize(instanceName));
        Directory.CreateDirectory(trashDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var destination = Path.Combine(trashDirectory, $"{stamp}-{Path.GetFileName(target)}");
        if (Directory.Exists(target))
        {
            Directory.Move(target, destination);
        }
        else
        {
            File.Move(target, destination);
        }

        return true;
    }

    /// <summary>启用/禁用：文件或目录改名为 &lt;name&gt;.disabled 或改回来。世界不支持。</summary>
    public bool SetEnabled(string instanceName, ContentKind kind, string name, bool enabled, string? world = null)
    {
        if (kind == ContentKind.Worlds)
        {
            throw new NotSupportedException("世界不支持启用/禁用");
        }

        ValidateBareName(name);
        var directory = DirectoryFor(instanceName, kind, world);
        var enabledPath = Path.Combine(directory, name);
        var disabledPath = enabledPath + DisabledSuffix;

        if (enabled)
        {
            if (!Exists(disabledPath))
            {
                return false;
            }

            Move(disabledPath, enabledPath);
            return true;
        }

        if (!Exists(enabledPath))
        {
            return false;
        }

        Move(enabledPath, disabledPath);
        return true;
    }

    private void EnsureInstance(string instanceName)
    {
        _ = instances.TryLoad(instanceName)
            ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
    }

    private static string RequireWorld(string? world)
    {
        if (string.IsNullOrWhiteSpace(world))
        {
            throw new ArgumentException("数据包需要指定世界（--world）");
        }

        ValidateBareName(world);
        return world;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void Move(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination, overwrite: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void ValidateBareName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/') || fileName.Contains('\\')
            || fileName is "." or "..")
        {
            throw new ArgumentException($"文件名不合法：'{fileName}'");
        }
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
