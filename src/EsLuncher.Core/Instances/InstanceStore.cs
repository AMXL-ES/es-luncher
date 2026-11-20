using System.IO.Compression;
using System.Text.Json;

namespace EsLuncher.Core.Instances;

/// <summary>
/// 一个实例的持久配置（JSON）。实例名允许中文与空格，但必须是合法的单层目录名。
/// </summary>
public sealed record InstanceConfig
{
    public required string Name { get; init; }

    public required string VersionId { get; init; }

    public int MemoryMb { get; init; } = 2048;

    /// <summary>显式指定 java.exe；空则按版本元数据自动选。</summary>
    public string? JavaPath { get; init; }

    /// <summary>离线用户名（未绑定账号时使用）。</summary>
    public string? Username { get; init; }

    /// <summary>已保存账号的键（DPAPI 存储）；空 = 离线身份。</summary>
    public string? AccountKey { get; init; }

    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];

    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];

    public int? WindowWidth { get; init; }

    public int? WindowHeight { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>实例名的合法性（允许中文/空格，禁止路径分隔符与 Windows 非法字符）。</summary>
public static class InstanceName
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("实例名不能为空");
        }

        if (name.Length > 64)
        {
            throw new ArgumentException("实例名不能超过 64 个字符");
        }

        if (name.IndexOfAny(InvalidChars) >= 0)
        {
            throw new ArgumentException($"实例名不能包含这些字符：{new string(InvalidChars)}");
        }

        if (name is "." or ".." || name.Trim() != name)
        {
            throw new ArgumentException("实例名不能是 . / ..，也不能以空格开头或结尾");
        }
    }
}

/// <summary>实例配置的读写（原子写：先写 .tmp 再替换）。</summary>
public sealed class InstanceStore(Downloads.DataLayout layout)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string Root => layout.InstancesDir;

    public IReadOnlyList<InstanceConfig> List()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        var configs = new List<InstanceConfig>();
        foreach (var directory in Directory.EnumerateDirectories(Root))
        {
            var file = Path.Combine(directory, "config.json");
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var config = JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(file), JsonOptions);
                if (config is not null)
                {
                    configs.Add(config);
                }
            }
            catch (JsonException)
            {
                // 单个坏配置不该让整个列表挂掉
            }
        }

        return configs.OrderBy(config => config.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public InstanceConfig? TryLoad(string name)
    {
        InstanceName.Validate(name);
        var file = layout.InstanceConfigFile(name);
        if (!File.Exists(file))
        {
            return null;
        }

        return JsonSerializer.Deserialize<InstanceConfig>(File.ReadAllText(file), JsonOptions);
    }

    public InstanceConfig Create(string name, string versionId, int memoryMb = 2048, string? username = null)
    {
        InstanceName.Validate(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);

        var directory = layout.InstanceDir(name);
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException($"实例已存在：{name}");
        }

        Directory.CreateDirectory(directory);
        var config = new InstanceConfig
        {
            Name = name,
            VersionId = versionId,
            MemoryMb = memoryMb,
            Username = string.IsNullOrWhiteSpace(username) ? "Player" : username,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Save(config);
        return config;
    }

    public void Save(InstanceConfig config)
    {
        InstanceName.Validate(config.Name);
        var file = layout.InstanceConfigFile(config.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(temporary, file, overwrite: true);
    }

    /// <summary>删除实例（含游戏目录）。必须显式 force，防手滑。</summary>
    public bool Delete(string name, bool force)
    {
        InstanceName.Validate(name);
        var directory = layout.InstanceDir(name);
        if (!Directory.Exists(directory))
        {
            return false;
        }

        if (!force)
        {
            throw new InvalidOperationException($"删除实例会连同游戏目录一起删除：{directory}。确认请加 --force");
        }

        Directory.Delete(directory, recursive: true);
        return true;
    }

    public InstanceConfig Clone(string source, string target)
    {
        var config = TryLoad(source) ?? throw new InvalidOperationException($"实例不存在：{source}");
        InstanceName.Validate(target);

        var targetDirectory = layout.InstanceDir(target);
        if (Directory.Exists(targetDirectory))
        {
            throw new InvalidOperationException($"目标实例已存在：{target}");
        }

        CopyDirectory(layout.InstanceDir(source), targetDirectory);
        var cloned = config with { Name = target, CreatedAt = DateTimeOffset.UtcNow };
        Save(cloned);
        return cloned;
    }

    public void Export(string name, string zipPath)
    {
        InstanceName.Validate(name);
        var directory = layout.InstanceDir(name);
        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"实例不存在：{name}");
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(zipPath));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(directory, zipPath);
    }

    public InstanceConfig Import(string zipPath, string name)
    {
        InstanceName.Validate(name);
        if (!File.Exists(zipPath))
        {
            throw new InvalidOperationException($"压缩包不存在：{zipPath}");
        }

        var directory = layout.InstanceDir(name);
        if (Directory.Exists(directory))
        {
            throw new InvalidOperationException($"实例已存在：{name}");
        }

        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(directory);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"压缩包内路径越界（zip slip）：'{entry.FullName}'");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            var config = TryLoad(name) ?? throw new InvalidDataException("导入的压缩包里没有 config.json");
            var imported = config with { Name = name, CreatedAt = DateTimeOffset.UtcNow };
            Save(imported);
            return imported;
        }
        catch
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }

            throw;
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
}
