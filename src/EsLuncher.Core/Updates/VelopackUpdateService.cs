using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace EsLuncher.Core.Updates;

/// <summary>
/// 基于 Velopack 的自动更新。
/// 更新源优先级：显式传入的本地目录 → 显式传入的 URL → Velopack 安装信息里的默认源。
/// 只有通过 Velopack Setup 安装过的程序才支持「应用更新并重启」；开发态只做检查。
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly string? _source;
    private readonly Action<string>? _log;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    /// <param name="sourcePathOrUrl">本地目录（验收/内网）或 http(s) 更新源；null = 用安装时的默认源。</param>
    /// <param name="log">可选的日志回调（UI 与 CLI 各自决定怎么落盘）。</param>
    public VelopackUpdateService(string? sourcePathOrUrl = null, Action<string>? log = null)
    {
        _source = sourcePathOrUrl;
        _log = log;
    }

    private UpdateManager Manager => _manager ??= CreateManager();

    private UpdateManager CreateManager()
    {
        var options = new UpdateOptions();
        if (!string.IsNullOrWhiteSpace(_source))
        {
            if (Directory.Exists(_source))
            {
                Log($"更新源：本地目录 {_source}");
                return new UpdateManager(new SimpleFileSource(new DirectoryInfo(_source)), options, null!);
            }

            Log($"更新源：{_source}");
            return new UpdateManager(_source, options, null!);
        }

        Log("更新源：使用 Velopack 安装信息里的默认源");
        return new UpdateManager(options, null!);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var manager = Manager;
        var current = manager.CurrentVersion?.ToString() ?? "0.0.0";
        Log($"检查更新：当前 {current}，已安装={manager.IsInstalled}，便携={manager.IsPortable}");

        try
        {
            _pending = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (NotInstalledException exception)
        {
            throw new InvalidOperationException(
                "当前程序不是安装形态（开发态或便携版），无法检查更新：请用 Setup.exe 安装后再试。",
                exception);
        }

        if (_pending is null)
        {
            Log("已是最新版本");
            return new UpdateCheckResult(manager.IsInstalled, manager.IsPortable, current, false, null, null, false);
        }

        var target = _pending.TargetFullRelease;
        var delta = _pending.DeltasToTarget is { Length: > 0 };
        Log($"发现新版本 {target.Version}（增量={delta}）");
        return new UpdateCheckResult(
            manager.IsInstalled,
            manager.IsPortable,
            current,
            true,
            target.Version?.ToString(),
            target.NotesMarkdown,
            delta);
    }

    public async Task DownloadAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var pending = Pending();
        Log($"开始下载 {pending.TargetFullRelease.Version}…");
        await Manager.DownloadUpdatesAsync(pending, progress, cancellationToken).ConfigureAwait(false);
        Log("下载完成");
    }

    public void ApplyAndRestart(string[]? restartArguments = null)
    {
        var pending = Pending();
        Log($"应用 {pending.TargetFullRelease.Version} 并重启");
        Manager.ApplyUpdatesAndRestart(pending.TargetFullRelease, restartArguments ?? []);
    }

    private UpdateInfo Pending()
        => _pending ?? throw new InvalidOperationException("还没有可下载的更新：先调用 CheckAsync。");

    private void Log(string message) => _log?.Invoke(message);
}
