using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.Core.Instances;

/// <summary>
/// 从别的启动器目录（.minecraft / Prism / MultiMC 的 game 目录）导入内容：
/// 复制 mods/config/saves 等子目录与 options.txt/servers.dat，不改源目录。
/// </summary>
public sealed class InstanceImporter(DataLayout layout, InstanceStore instances)
{
    private static readonly string[] ContentDirectories =
    [
        "mods", "config", "defaultconfigs", "saves", "resourcepacks", "shaderpacks", "screenshots", "flaremaps",
    ];

    private static readonly string[] RootFiles = ["options.txt", "servers.dat", "servers.dat_old"];

    public InstanceConfig Import(string name, string sourceDirectory, string versionId, int memoryMb = 2048)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"源目录不存在：{sourceDirectory}");
        }

        var instance = instances.Create(name, versionId, memoryMb, username: null);
        var gameDirectory = layout.GameDir(instance.Name);
        Directory.CreateDirectory(gameDirectory);

        foreach (var subdirectory in ContentDirectories)
        {
            var source = Path.Combine(sourceDirectory, subdirectory);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(gameDirectory, subdirectory));
            }
        }

        foreach (var file in RootFiles)
        {
            var source = Path.Combine(sourceDirectory, file);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(gameDirectory, file), overwrite: true);
            }
        }

        return instance;
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
