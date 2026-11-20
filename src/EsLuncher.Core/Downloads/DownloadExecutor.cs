using System.Collections.Concurrent;

namespace EsLuncher.Core.Downloads;

/// <summary>
/// 并发执行下载计划。默认并发 8（用户已裁剪）；进度按"完成文件数"回报，
/// 单文件失败不中断整体，最后汇总失败清单。
/// </summary>
public sealed class DownloadExecutor
{
    private readonly FileDownloader _downloader;

    public DownloadExecutor(FileDownloader downloader, int parallelism = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        _downloader = downloader;
        Parallelism = parallelism;
    }

    public int Parallelism { get; }

    public async Task<DownloadReport> RunAsync(
        DownloadPlan plan,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var total = plan.Files.Count;
        if (total == 0)
        {
            return new DownloadReport(0, 0, 0, 0, 0, []);
        }

        long done = 0;
        long bytes = 0;
        var downloaded = 0;
        var skipped = 0;
        var failed = 0;
        var failures = new ConcurrentBag<DownloadFailure>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Parallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(plan.Files, options, async (job, token) =>
        {
            var result = await _downloader.DownloadAsync(job, token).ConfigureAwait(false);
            switch (result.Status)
            {
                case FileDownloadStatus.Downloaded:
                    Interlocked.Increment(ref downloaded);
                    break;
                case FileDownloadStatus.AlreadyPresent:
                    Interlocked.Increment(ref skipped);
                    break;
                default:
                    Interlocked.Increment(ref failed);
                    failures.Add(new DownloadFailure(job.TargetPath, result.Error ?? "未知错误"));
                    break;
            }

            Interlocked.Add(ref bytes, result.Bytes);
            var filesDone = Interlocked.Increment(ref done);
            progress?.Report(new DownloadProgress(filesDone, total, Interlocked.Read(ref bytes), job.TargetPath));
        }).ConfigureAwait(false);

        return new DownloadReport(
            total,
            downloaded,
            skipped,
            failed,
            Interlocked.Read(ref bytes),
            failures.ToArray());
    }
}
