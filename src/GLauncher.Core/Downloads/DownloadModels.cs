namespace GLauncher.Core.Downloads;

/// <summary>一个下载任务：主源 + 若干镜像，落盘到 <see cref="TargetPath"/>。</summary>
public sealed record DownloadJob
{
    public required Uri Primary { get; init; }

    public IReadOnlyList<Uri> Mirrors { get; init; } = [];

    public required string TargetPath { get; init; }

    public long? ExpectedSize { get; init; }

    public string? ExpectedSha1 { get; init; }
}

/// <summary>解压一个 natives jar 到目标目录；<see cref="ExcludePrefixes"/> 通常是 <c>META-INF/</c>。</summary>
public sealed record NativeExtraction(string JarPath, string TargetDirectory, IReadOnlyList<string> ExcludePrefixes);

public sealed record DownloadPlan(IReadOnlyList<DownloadJob> Files, IReadOnlyList<NativeExtraction> Natives)
{
    public static DownloadPlan Empty { get; } = new([], []);
}

public enum FileDownloadStatus
{
    Downloaded,
    AlreadyPresent,
    Failed,
}

public sealed record FileDownloadResult(FileDownloadStatus Status, long Bytes, int Attempts, string? Error)
{
    public static FileDownloadResult Failed(string error) => new(FileDownloadStatus.Failed, 0, 0, error);
}

public sealed record DownloadProgress(long FilesDone, long FilesTotal, long BytesDownloaded, string CurrentPath);

public sealed record DownloadFailure(string Path, string Error);

public sealed record DownloadReport(
    int Total,
    int Downloaded,
    int Skipped,
    int Failed,
    long BytesDownloaded,
    IReadOnlyList<DownloadFailure> Failures)
{
    public bool Success => Failed == 0;
}
