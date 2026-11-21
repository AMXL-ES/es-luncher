namespace EsLuncher.Core.Updates;

/// <summary>
/// 检查更新的结果。版本号一律用字符串，避免把 Velopack 的 SemVer 类型泄漏到公共 API。
/// </summary>
public sealed record UpdateCheckResult(
    bool IsInstalled,
    bool IsPortable,
    string CurrentVersion,
    bool UpdateAvailable,
    string? AvailableVersion,
    string? ReleaseNotes,
    bool DeltaAvailable);

/// <summary>
/// 自动更新：检查 → 下载 → 应用并重启。实现取 Velopack（MIT、原生支持 .NET 10），
/// 接口留给测试替身，将来换实现（GitHub / 自建源）只换 IUpdateSource。
/// </summary>
public interface IUpdateService
{
    /// <summary>查更新源，返回是否有新版本。未检查过就调用 <see cref="DownloadAsync"/> 会抛异常。</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>下载已检查到的更新；<paramref name="progress"/> 是 0–100 的百分比。</summary>
    Task DownloadAsync(Action<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>应用已下载的更新并重启进程（Velopack 会接管后续替换）。</summary>
    void ApplyAndRestart(string[]? restartArguments = null);
}
