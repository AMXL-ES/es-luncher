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
        CancellationToken cancellationToken)
    {
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var catalog = new VersionCatalog(layout, downloader);

        report($"读取版本 {instance.VersionId} …");
        var manifest = await catalog.GetManifestAsync(false, cancellationToken).ConfigureAwait(false);
        var version = await catalog.GetVersionAsync(instance.VersionId, manifest, false, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(version.InheritsFrom))
        {
            var parents = new Dictionary<string, VersionMetadata>(StringComparer.Ordinal);
            var pending = version;
            while (!string.IsNullOrWhiteSpace(pending.InheritsFrom))
            {
                var parentId = pending.InheritsFrom!;
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
        if (!selection.IsSatisfied)
        {
            return new LaunchOutcome(false, selection.Message);
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
}
