using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Content;

public enum ContentKind
{
    Mods,
    ResourcePacks,
    ShaderPacks,
}

public sealed record ContentFile(string Name, long Size, DateTimeOffset Modified);

/// <summary>
/// 实例内容管理：mods / 资源包 / 光影包。
/// 删除一律"移入回收目录"而不是真删，避免误操作毁存档；目录结构保持简单，便于后续加依赖解析。
/// </summary>
public sealed class InstanceContentService(DataLayout layout, InstanceStore instances)
{
    public static string FolderName(ContentKind kind) => kind switch
    {
        ContentKind.Mods => "mods",
        ContentKind.ResourcePacks => "resourcepacks",
        ContentKind.ShaderPacks => "shaderpacks",
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
            default:
                kind = default;
                return false;
        }
    }

    public string DirectoryFor(string instanceName, ContentKind kind)
    {
        EnsureInstance(instanceName);
        return Path.Combine(layout.GameDir(instanceName), FolderName(kind));
    }

    public IReadOnlyList<ContentFile> List(string instanceName, ContentKind kind)
    {
        var directory = DirectoryFor(instanceName, kind);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory)
            .Select(path => new ContentFile(Path.GetFileName(path), new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public ContentFile Add(string instanceName, ContentKind kind, string sourcePath)
    {
        var directory = DirectoryFor(instanceName, kind);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"源文件不存在：{sourcePath}", sourcePath);
        }

        Directory.CreateDirectory(directory);
        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException($"无法从路径得到文件名：{sourcePath}");
        }

        var target = Path.Combine(directory, fileName);
        File.Copy(sourcePath, target, overwrite: true);
        var info = new FileInfo(target);
        return new ContentFile(info.Name, info.Length, info.LastWriteTimeUtc);
    }

    /// <summary>移除内容：移到 数据根/trash/content/&lt;实例&gt;/&lt;时间戳&gt;-&lt;文件名&gt;，返回是否找到。</summary>
    public bool Remove(string instanceName, ContentKind kind, string fileName)
    {
        ValidateBareFileName(fileName);
        var directory = DirectoryFor(instanceName, kind);
        var source = Path.Combine(directory, fileName);
        if (!File.Exists(source))
        {
            return false;
        }

        var trashDirectory = Path.Combine(layout.Root, "trash", "content", Sanitize(instanceName));
        Directory.CreateDirectory(trashDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        File.Move(source, Path.Combine(trashDirectory, $"{stamp}-{fileName}"), overwrite: false);
        return true;
    }

    private void EnsureInstance(string instanceName)
    {
        _ = instances.TryLoad(instanceName)
            ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
    }

    private static void ValidateBareFileName(string fileName)
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
