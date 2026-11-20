using System.Security.Cryptography;
using EsLuncher.Core.Downloads;

namespace EsLuncher.Core.Tests;

public sealed class FileDownloaderTests : IDisposable
{
    private readonly string _root;
    private readonly FileDownloader _downloader;
    private readonly FakeRemoteFileClient _client;

    public FileDownloaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-dl-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _client = new FakeRemoteFileClient();
        _downloader = new FileDownloader(_client);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 测试清理失败不影响结论
        }
    }

    [Fact]
    public async Task Downloads_and_verifies_sha1()
    {
        var content = "hello minecraft"u8.ToArray();
        var sha1 = Sha1(content);
        _client.OnGet = (_, _, _) => Task.FromResult(FakeRemoteFileClient.Ok(content));
        var target = Path.Combine(_root, "a.txt");

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/a"),
                TargetPath = target,
                ExpectedSize = content.Length,
                ExpectedSha1 = sha1,
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(content.Length, result.Bytes);
        Assert.Equal("hello minecraft", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task Skips_when_existing_file_matches()
    {
        var content = "already here"u8.ToArray();
        _client.OnGet = (_, _, _) => throw new InvalidOperationException("已存在时不应发请求");
        var target = Path.Combine(_root, "existing.txt");
        await File.WriteAllBytesAsync(target, content);

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/existing"),
                TargetPath = target,
                ExpectedSize = content.Length,
                ExpectedSha1 = Sha1(content),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.AlreadyPresent, result.Status);
        Assert.Empty(_client.Requests);
    }

    [Fact]
    public async Task Retries_server_errors_then_succeeds()
    {
        var content = "retry me"u8.ToArray();
        var calls = 0;
        _client.OnGet = (_, _, _) =>
        {
            calls++;
            return calls < 3
                ? Task.FromResult(new RemoteFileResponse(500, null, null))
                : Task.FromResult(FakeRemoteFileClient.Ok(content));
        };

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/retry"),
                TargetPath = Path.Combine(_root, "retry.txt"),
                ExpectedSha1 = Sha1(content),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Falls_back_to_mirror_on_connection_failure()
    {
        var content = "from mirror"u8.ToArray();
        var primary = new Uri("https://primary.test/x");
        var mirror = new Uri("https://mirror.test/x");
        _client.OnGet = (uri, _, _) => uri == primary
            ? throw new HttpRequestException("no route")
            : Task.FromResult(FakeRemoteFileClient.Ok(content));

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = primary,
                Mirrors = [mirror],
                TargetPath = Path.Combine(_root, "mirror.txt"),
                ExpectedSha1 = Sha1(content),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(2, _client.Requests.Count);
        Assert.Equal("https://mirror.test/x", _client.Requests[1].Url);
    }

    [Fact]
    public async Task Resumes_from_partial_file_with_range_request()
    {
        var content = "0123456789"u8.ToArray();
        var partialLength = 4;
        var target = Path.Combine(_root, "resume.bin");
        await File.WriteAllBytesAsync(target + ".part", content[..partialLength]);
        _client.OnGet = (_, rangeFrom, _) =>
        {
            if (rangeFrom != partialLength)
            {
                throw new InvalidOperationException($"期望从 {partialLength} 续传，实际 {rangeFrom}");
            }

            return Task.FromResult(FakeRemoteFileClient.Partial(content[partialLength..], content.Length));
        };

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/resume"),
                TargetPath = target,
                ExpectedSize = content.Length,
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Restarts_when_server_ignores_range()
    {
        var content = "abcdefghij"u8.ToArray();
        var target = Path.Combine(_root, "ignore-range.bin");
        await File.WriteAllBytesAsync(target + ".part", "XXXX"u8.ToArray());
        _client.OnGet = (_, _, _) => Task.FromResult(FakeRemoteFileClient.Ok(content));

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/ignored"),
                TargetPath = target,
                ExpectedSize = content.Length,
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(content, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Fails_after_all_sources_and_attempts_are_exhausted()
    {
        _client.OnGet = (_, _, _) => Task.FromResult(new RemoteFileResponse(503, null, null));

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://primary.test/f"),
                Mirrors = [new Uri("https://mirror.test/f")],
                TargetPath = Path.Combine(_root, "fail.bin"),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Failed, result.Status);
        Assert.Equal(6, result.Attempts); // 2 源 × 3 次
        Assert.Contains("503", result.Error);
    }

    [Fact]
    public async Task Sha1_mismatch_deletes_partial_and_redownloads()
    {
        var good = "good"u8.ToArray();
        var bad = "bad!"u8.ToArray();
        var calls = 0;
        _client.OnGet = (_, _, _) =>
        {
            calls++;
            return Task.FromResult(FakeRemoteFileClient.Ok(calls == 1 ? bad : good));
        };

        var target = Path.Combine(_root, "corrupt.bin");
        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = new Uri("https://example.test/corrupt"),
                TargetPath = target,
                ExpectedSha1 = Sha1(good),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(good, await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task Missing_file_on_primary_goes_to_mirror_without_retry()
    {
        var content = "ok"u8.ToArray();
        var primary = new Uri("https://primary.test/missing");
        var mirror = new Uri("https://mirror.test/missing");
        _client.OnGet = (uri, _, _) => Task.FromResult(uri == primary
            ? FakeRemoteFileClient.NotFound()
            : FakeRemoteFileClient.Ok(content));

        var result = await _downloader.DownloadAsync(
            new DownloadJob
            {
                Primary = primary,
                Mirrors = [mirror],
                TargetPath = Path.Combine(_root, "missing.bin"),
                ExpectedSha1 = Sha1(content),
            },
            CancellationToken.None);

        Assert.Equal(FileDownloadStatus.Downloaded, result.Status);
        Assert.Equal(2, _client.Requests.Count);
    }

    private static string Sha1(byte[] content) => Convert.ToHexStringLower(SHA1.HashData(content));
}
