namespace GLauncher.Core.Metadata;

/// <summary>
/// 一个可下载对象。现代 version.json 里 <c>downloads.artifact</c> /
/// <c>downloads.client</c> / <c>logging.client.file</c> 都是这个形状。
/// <c>Size</c>/<c>Sha1</c> 在极老的库条目里可能缺失，所以可空。
/// </summary>
public sealed record DownloadArtifact(string Url, long? Size, string? Sha1)
{
    /// <summary>相对 maven 仓库的路径（<c>downloads.artifact.path</c>）。client/server 没有。</summary>
    public string? Path { get; init; }
}
