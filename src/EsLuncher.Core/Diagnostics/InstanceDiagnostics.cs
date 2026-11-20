using System.IO.Compression;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;

namespace EsLuncher.Core.Diagnostics;

public sealed record CrashReportInfo(string Path, string Summary);

public sealed record DiagnosticReport(
    string Instance,
    string? LogPath,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CrashReportInfo> Crashes,
    IReadOnlyList<string> Hints);

/// <summary>
/// 实例诊断：扫 latest.log 的错误行、crash-reports、给出人话提示；可打包成诊断 zip。
/// 只读，不修改玩家任何文件。
/// </summary>
public sealed class InstanceDiagnostics(DataLayout layout, InstanceStore instances)
{
    private const int MaxLogLines = 4000;
    private const int MaxErrors = 60;

    public DiagnosticReport Analyze(string instanceName)
    {
        var instance = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        var gameDirectory = layout.GameDir(instance.Name);
        var logPath = Path.Combine(gameDirectory, "logs", "latest.log");

        var errors = new List<string>();
        if (File.Exists(logPath))
        {
            foreach (var line in File.ReadLines(logPath).TakeLast(MaxLogLines))
            {
                if (errors.Count >= MaxErrors)
                {
                    break;
                }

                if (line.Contains("ERROR]", StringComparison.Ordinal)
                    || line.Contains("FATAL]", StringComparison.Ordinal)
                    || line.Contains("Exception", StringComparison.Ordinal)
                    || line.Contains("Caused by", StringComparison.Ordinal))
                {
                    errors.Add(line.Trim());
                }
            }
        }

        var crashes = new List<CrashReportInfo>();
        var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
        if (Directory.Exists(crashDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(crashDirectory, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
            {
                var summary = File.ReadLines(file).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "(空)";
                crashes.Add(new CrashReportInfo(file, summary.Trim()));
            }
        }

        var hints = BuildHints(errors, crashes).ToList();
        try
        {
            var dependencies = new DependencyChecker(new InstanceContentService(layout, instances)).Check(instance.Name);
            if (dependencies.Missing.Count > 0)
            {
                hints.Add($"缺少模组前置：{string.Join(", ", dependencies.Missing)}（可用 `esluncher modrinth install` 补装）");
            }
        }
        catch (IOException)
        {
            // 依赖体检失败不影响主诊断
        }

        return new DiagnosticReport(instance.Name, File.Exists(logPath) ? logPath : null, errors, crashes, hints);
    }

    public string Export(string instanceName, string outZip)
    {
        var instance = instances.TryLoad(instanceName) ?? throw new InvalidOperationException($"实例不存在：{instanceName}");
        var gameDirectory = layout.GameDir(instance.Name);
        var directory = Path.GetDirectoryName(Path.GetFullPath(outZip));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outZip))
        {
            File.Delete(outZip);
        }

        using var archive = ZipFile.Open(outZip, ZipArchiveMode.Create);
        AddFileIfExists(archive, Path.Combine(gameDirectory, "logs", "latest.log"), "logs/latest.log");
        var crashDirectory = Path.Combine(gameDirectory, "crash-reports");
        if (Directory.Exists(crashDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(crashDirectory, "*.txt"))
            {
                AddFileIfExists(archive, file, "crash-reports/" + Path.GetFileName(file));
            }
        }

        AddFileIfExists(archive, layout.InstanceConfigFile(instance.Name), "instance/config.json");
        AddText(archive, "system-info.txt", BuildSystemInfo(instance.Name));
        return outZip;
    }

    private static void AddFileIfExists(ZipArchive archive, string path, string entryName)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var entry = archive.CreateEntry(entryName);
        using var source = File.OpenRead(path);
        using var target = entry.Open();
        source.CopyTo(target);
    }

    private static void AddText(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static string BuildSystemInfo(string instanceName)
    {
        var java = string.Join(
            Environment.NewLine,
            JavaDiscovery.Discover([new DataLayout(Environment.GetEnvironmentVariable("ESLUNCHER_DATA") ?? @"D:\mc-data").JavaDir])
                .Select(runtime => $"  Java {runtime.MajorVersion} @ {runtime.JavaExePath}"));

        return $"""
            instance: {instanceName}
            os: {Environment.OSVersion}
            arch: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}
            runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}
            locale: {System.Globalization.CultureInfo.CurrentCulture}
            time: {DateTimeOffset.Now:O}
            java:
            {java}
            """;
    }

    private static IReadOnlyList<string> BuildHints(IReadOnlyList<string> errors, IReadOnlyList<CrashReportInfo> crashes)
    {
        var hints = new List<string>();
        var joined = string.Join('\n', errors);

        if (joined.Contains("UnsupportedClassVersionError", StringComparison.Ordinal))
        {
            hints.Add("Java 版本不匹配：这个版本需要更高/更低的 Java，请用 `esluncher java install --version <id>` 并检查实例的 Java 设置。");
        }

        if (joined.Contains("OutOfMemoryError", StringComparison.Ordinal))
        {
            hints.Add("内存不足：调大实例内存（当前默认 2048 MB），或关闭其他占内存的程序。");
        }

        if (joined.Contains("NoClassDefFoundError", StringComparison.Ordinal)
            || joined.Contains("ClassNotFoundException", StringComparison.Ordinal)
            || joined.Contains("Missing or unsupported mandatory dependencies", StringComparison.Ordinal)
            || joined.Contains("Incompatible mods found", StringComparison.Ordinal)
            || joined.Contains("HARD_DEP", StringComparison.Ordinal))
        {
            hints.Add("疑似缺少模组依赖：检查 mods 目录里是否缺前置（Fabric API / 依赖 mod），或加载器版本与 MC 版本不匹配。");
        }

        if (joined.Contains("Failed to create window", StringComparison.Ordinal)
            || joined.Contains("GLFW", StringComparison.Ordinal)
            || joined.Contains("OpenGL", StringComparison.Ordinal))
        {
            hints.Add("图形初始化失败：更新显卡驱动，或检查是否在远程桌面/虚拟机里启动。");
        }

        if (crashes.Count > 0)
        {
            hints.Add($"发现 {crashes.Count} 份崩溃报告，建议看 crash-reports 里最新一份的 'Description' 段。");
        }

        return hints;
    }
}
