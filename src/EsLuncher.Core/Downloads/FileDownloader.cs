namespace EsLuncher.Core.Downloads;

/// <summary>某个源明确不可用（4xx 等），不必重试，直接换下一个源。</summary>
internal sealed class SourceUnavailableException(string message) : Exception(message);

/// <summary>可重试的 HTTP 状态（5xx / 408 / 429）：同一个源再试，不急着换镜像。</summary>
internal sealed class TransientHttpException(string message) : Exception(message);

/// <summary>
/// 单文件下载器：主源 → 镜像回退；每个源最多重试 3 次；支持 Range 续传；
/// 大小/哈希校验不过就重下（哈希不符时删除半成品）。
/// </summary>
public sealed class FileDownloader
{
    public const int MaxAttemptsPerSource = 3;

    private readonly IRemoteFileClient _client;

    public FileDownloader(IRemoteFileClient client)
    {
        _client = client;
    }

    public async Task<FileDownloadResult> DownloadAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(job.TargetPath);

        var directory = Path.GetDirectoryName(job.TargetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (await IsUpToDateAsync(job, cancellationToken).ConfigureAwait(false))
        {
            return new FileDownloadResult(FileDownloadStatus.AlreadyPresent, 0, 0, null);
        }

        var sources = new List<Uri> { job.Primary };
        sources.AddRange(job.Mirrors);

        var errors = new List<string>();
        var attempts = 0;

        foreach (var source in sources)
        {
            for (var attempt = 1; attempt <= MaxAttemptsPerSource; attempt++)
            {
                attempts++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bytes = await TryDownloadFromSourceAsync(job, source, cancellationToken).ConfigureAwait(false);
                    return new FileDownloadResult(FileDownloadStatus.Downloaded, bytes, attempts, null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (SourceUnavailableException ex)
                {
                    // 404/403 这类"这个源没有这个文件"：重试无意义，换镜像。
                    errors.Add($"{source}: {ex.Message}");
                    break;
                }
                catch (TransientHttpException ex)
                {
                    errors.Add($"{source}: {ex.Message}");
                }
                catch (HttpRequestException ex)
                {
                    // 连不上：按既定策略立刻换镜像，不浪费重试预算。
                    errors.Add($"{source}: {ex.Message}");
                    break;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    errors.Add($"{source}: {ex.Message}");
                }
            }
        }

        return new FileDownloadResult(FileDownloadStatus.Failed, 0, attempts, string.Join(" | ", errors));
    }

    private async Task<bool> IsUpToDateAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        if (!File.Exists(job.TargetPath))
        {
            return false;
        }

        var info = new FileInfo(job.TargetPath);
        if (job.ExpectedSize is long expectedSize && info.Length != expectedSize)
        {
            return false;
        }

        if (job.ExpectedSha1 is { } expectedSha1)
        {
            return await Sha1Util.MatchesAsync(job.TargetPath, expectedSha1, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<long> TryDownloadFromSourceAsync(DownloadJob job, Uri source, CancellationToken cancellationToken)
    {
        var partialPath = job.TargetPath + ".part";
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        using var response = await _client.GetAsync(source, existingBytes, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var message = $"HTTP {response.StatusCode}";
            if (response.StatusCode is >= 500 or 408 or 429)
            {
                throw new TransientHttpException(message);
            }

            throw new SourceUnavailableException(message);
        }

        // 服务端忽略 Range（返回 200）时必须从头写，否则文件会拼坏。
        var append = response.StatusCode == 206 && existingBytes > 0;
        await using (var file = new FileStream(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            useAsync: true))
        {
            await response.Content!.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var length = new FileInfo(partialPath).Length;

        if (job.ExpectedSize is long expectedSize)
        {
            if (length > expectedSize)
            {
                File.Delete(partialPath);
                throw new InvalidDataException($"下载超出预期大小：{length} > {expectedSize}");
            }

            if (length != expectedSize)
            {
                throw new InvalidDataException($"下载不完整：{length} / {expectedSize}（保留 .part 供续传）");
            }
        }

        if (job.ExpectedSha1 is { } expectedSha1)
        {
            if (!await Sha1Util.MatchesAsync(partialPath, expectedSha1, cancellationToken).ConfigureAwait(false))
            {
                File.Delete(partialPath);
                throw new InvalidDataException($"SHA1 不匹配（期望 {expectedSha1}）");
            }
        }

        File.Move(partialPath, job.TargetPath, overwrite: true);
        return length;
    }
}
