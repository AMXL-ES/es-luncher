using GLauncher.Core.Downloads;
using GLauncher.Core.Launching;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Java;

/// <summary>
/// 安装 Mojang 官方 Java 运行时（JDK/JRE 由 Mojang 分发，不需要系统安装）。
/// 落盘 <c>D:\mc-data\java\&lt;组件&gt;\&lt;版本&gt;\</c>，与系统 JDK 完全隔离。
/// </summary>
public sealed class JavaRuntimeInstaller(
    DataLayout layout,
    FileDownloader downloader,
    Func<string, JavaRuntime?>? probe = null)
{
    private readonly Func<string, JavaRuntime?> _probe = probe ?? JavaRuntime.TryProbe;
    public async Task<JavaRuntime> InstallAsync(
        JavaRuntimePackage package,
        int concurrency,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installDir = Path.Combine(layout.JavaDir, package.Component, package.VersionName);
        Directory.CreateDirectory(installDir);

        var manifestPath = Path.Combine(installDir, ".runtime-manifest.json");
        var manifest = await downloader.DownloadAsync(
                new DownloadJob
                {
                    Primary = package.ManifestUri,
                    TargetPath = manifestPath,
                    ExpectedSha1 = package.ManifestSha1,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (manifest.Status == FileDownloadStatus.Failed)
        {
            throw new InvalidOperationException($"下载 {package.Component} 的运行时清单失败：{manifest.Error}");
        }

        var files = JavaRuntimeManifestParser.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var jobs = new List<DownloadJob>();
        foreach (var file in files)
        {
            var target = ResolveTarget(installDir, file.Path);
            switch (file.Type)
            {
                case RuntimeEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;
                case RuntimeEntryType.Link:
                    // 官方 Windows 运行时目前没有 link；即使有也不跟随，避免越界。
                    break;
                case RuntimeEntryType.File when file.Url is not null && file.Sha1 is not null:
                    jobs.Add(new DownloadJob
                    {
                        Primary = new Uri(file.Url),
                        TargetPath = target,
                        ExpectedSize = file.Size,
                        ExpectedSha1 = file.Sha1,
                    });
                    break;
            }
        }

        var executor = new DownloadExecutor(downloader, concurrency);
        var report = await executor.RunAsync(new DownloadPlan(jobs, []), progress, cancellationToken)
            .ConfigureAwait(false);
        if (!report.Success)
        {
            var first = report.Failures.Count > 0 ? report.Failures[0].Error : "未知错误";
            throw new InvalidOperationException($"运行时下载失败 {report.Failed}/{report.Total}：{first}");
        }

        ApplyExecutableBits(files, installDir);

        var javaPath = Path.Combine(installDir, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
        return _probe(javaPath)
            ?? throw new InvalidOperationException($"运行时已落盘但探测失败：{javaPath}");
    }

    /// <summary>把运行时相对路径解析到安装目录内；越界（<c>..</c>）直接拒绝。</summary>
    public static string ResolveTarget(string installDir, string relativePath)
    {
        var root = Path.GetFullPath(installDir);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new MetadataFormatException("$.files", $"运行时文件路径越界：'{relativePath}'");
        }

        return target;
    }

    private static void ApplyExecutableBits(IReadOnlyList<RuntimeFile> files, string installDir)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var file in files)
        {
            if (!file.Executable || file.Type != RuntimeEntryType.File)
            {
                continue;
            }

            var target = ResolveTarget(installDir, file.Path);
            if (File.Exists(target))
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
    }
}
