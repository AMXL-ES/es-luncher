using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Loaders;
using EsLuncher.Core.Metadata;

namespace EsLuncher.App;

public sealed record LaunchOutcome(bool Success, string Message);

/// <summary>
/// UI 侧的启动编排：解析继承链、选 Java、组装命令行、补 natives、记录日志。
/// 所有重活都在 EsLuncher.Core，这里只负责串起来并把进度文本交给界面。
/// </summary>
public sealed class LaunchService(DataLayout layout)
{
    public async Task<LaunchOutcome> LaunchAsync(
        InstanceConfig instance,
        Action<string> report,
        CancellationToken cancellationToken,
        bool autoInstallJava = false)
    {
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var catalog = new VersionCatalog(layout, downloader);

        report($"读取版本 {instance.VersionId} …");
        var manifest = await catalog.GetManifestAsync(false, cancellationToken).ConfigureAwait(false);
        var version = await catalog.GetVersionAsync(instance.VersionId, manifest, false, cancellationToken)
            .ConfigureAwait(false);

        var rootVersionId = version.Id;
        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            var parents = new Dictionary<string, VersionMetadata>(StringComparer.Ordinal);
            var pending = version;
            while (!string.IsNullOrWhiteSpace(pending.InheritsFrom))
            {
                var parentId = pending.InheritsFrom!;
                rootVersionId = parentId;
                if (!parents.TryGetValue(parentId, out var parent))
                {
                    parent = await catalog.GetVersionAsync(parentId, manifest, false, cancellationToken).ConfigureAwait(false);
                    parents[parentId] = parent;
                }

                pending = parent;
            }

            version = VersionMerger.Resolve(version, id => parents[id]);
        }

        var environment = LaunchEnvironment.Detect();
        var required = version.JavaVersion?.MajorVersion ?? 8;
        var selection = JavaSelector.Select(required, JavaDiscovery.Discover([layout.JavaDir]), null);

        if (!selection.IsSatisfied && autoInstallJava)
        {
            report($"缺少 Java {required}，自动下载官方运行时 …");
            try
            {
                var catalogJson = await ReadStringAsync(JavaRuntimeCatalog.AllRuntimes, cancellationToken).ConfigureAwait(false);
                var packages = JavaRuntimeCatalog.Parse(catalogJson, JavaPlatform.Detect());
                var component = version.JavaVersion?.Component ?? ComponentForMajor(required);
                var package = JavaRuntimeCatalog.Find(packages, component)
                    ?? JavaRuntimeCatalog.Find(packages, "java-runtime-gamma")
                    ?? packages.FirstOrDefault();
                if (package is null)
                {
                    return new LaunchOutcome(false, $"官方目录里没有可用的 Java {required} 运行时");
                }

                var installer = new JavaRuntimeInstaller(layout, downloader);
                await installer.InstallAsync(package, 4, null, cancellationToken).ConfigureAwait(false);
                selection = JavaSelector.Select(required, JavaDiscovery.Discover([layout.JavaDir]), null);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                return new LaunchOutcome(false, $"自动下载 Java 失败：{exception.Message}");
            }
        }

        if (!selection.IsSatisfied)
        {
            return new LaunchOutcome(false, selection.Message + "（可在设置里打开「缺少 Java 时自动下载」）");
        }

        // 预检：客户端 jar 必须存在（loader 版本会继承原版 jar）
        if (!File.Exists(layout.ClientJar(rootVersionId)) && !File.Exists(layout.ClientJar(version.Id)))
        {
            return new LaunchOutcome(false, $"缺少客户端 jar（{rootVersionId}），请先在下载页下载该版本");
        }

        var request = new LaunchRequest
        {
            Username = string.IsNullOrWhiteSpace(instance.Username) ? "Player" : instance.Username,
            InstanceId = instance.Name,
            MemoryMb = instance.MemoryMb,
            ExtraJvmArguments = instance.ExtraJvmArguments,
            ExtraGameArguments = instance.ExtraGameArguments,
        };
        var command = LaunchCommandBuilder.Build(version, environment, layout, selection.Runtime!, request);
        VersionFetcher.EnsureNatives(version, environment, layout, instance.Name);

        Directory.CreateDirectory(command.WorkingDirectory);
        var logPath = Path.Combine(
            layout.LogsDir,
            $"ui-{Sanitize(instance.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4]}.log");

        report($"启动 {version.Id}（Java {required} · {instance.MemoryMb} MB）…");
        var result = await ProcessRunner.RunLoggedAsync(command, logPath, null, cancellationToken)
            .ConfigureAwait(false);

        return new LaunchOutcome(
            result.ExitCode == 0,
            $"游戏退出码 {result.ExitCode}，时长 {result.Duration.TotalSeconds:F0}s\n日志：{logPath}");
    }

    private static string Sanitize(string name)
        => string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private static string ComponentForMajor(int major) => major switch
    {
        8 => "jre-legacy",
        16 => "java-runtime-alpha",
        17 => "java-runtime-beta",
        21 => "java-runtime-delta",
        _ => "java-runtime-gamma",
    };

    private async Task<string> ReadStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var client = new HttpRemoteFileClient();
        using var response = await client.GetAsync(uri, 0, cancellationToken).ConfigureAwait(false);
        if (response.Content is null)
        {
            throw new HttpRequestException($"读取失败：{uri}");
        }

        using var reader = new StreamReader(response.Content);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
