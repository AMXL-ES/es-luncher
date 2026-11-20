using System.Security.Cryptography;
using EsLuncher.Core.Downloads;

namespace EsLuncher.Core.Tests;

public sealed class DownloadExecutorTests : IDisposable
{
    private readonly string _root;
    private readonly FakeRemoteFileClient _client;
    private readonly FileDownloader _downloader;

    public DownloadExecutorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "esluncher-exec-tests", Guid.NewGuid().ToString("N"));
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
        }
    }

    [Fact]
    public async Task Runs_all_jobs_and_reports_counts()
    {
        var contents = new Dictionary<string, byte[]>
        {
            ["https://example.test/1"] = "one"u8.ToArray(),
            ["https://example.test/2"] = "two-two"u8.ToArray(),
            ["https://example.test/3"] = "three-three-three"u8.ToArray(),
        };
        _client.OnGet = (uri, _, _) => Task.FromResult(FakeRemoteFileClient.Ok(contents[uri.AbsoluteUri]));

        var plan = new DownloadPlan(
            contents.Select(kv => new DownloadJob
            {
                Primary = new Uri(kv.Key),
                TargetPath = Path.Combine(_root, Path.GetFileName(kv.Key) + ".bin"),
                ExpectedSha1 = Convert.ToHexStringLower(SHA1.HashData(kv.Value)),
            }).ToArray(),
            []);

        var executor = new DownloadExecutor(_downloader, parallelism: 3);
        var report = await executor.RunAsync(plan, progress: null, CancellationToken.None);

        Assert.True(report.Success);
        Assert.Equal(3, report.Downloaded);
        Assert.Equal(0, report.Failed);
        Assert.Equal(contents.Values.Sum(v => (long)v.Length), report.BytesDownloaded);
        foreach (var (url, bytes) in contents)
        {
            var path = Path.Combine(_root, Path.GetFileName(url) + ".bin");
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_rest()
    {
        _client.OnGet = (uri, _, _) => Task.FromResult(uri.AbsolutePath == "/bad"
            ? new RemoteFileResponse(500, null, null)
            : FakeRemoteFileClient.Ok("ok"u8.ToArray()));

        var plan = new DownloadPlan(
            [
                new DownloadJob { Primary = new Uri("https://example.test/bad"), TargetPath = Path.Combine(_root, "bad.bin") },
                new DownloadJob { Primary = new Uri("https://example.test/good"), TargetPath = Path.Combine(_root, "good.bin") },
            ],
            []);

        var executor = new DownloadExecutor(_downloader, parallelism: 2);
        var report = await executor.RunAsync(plan, progress: null, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Equal(1, report.Failed);
        Assert.Equal(1, report.Downloaded);
        Assert.Single(report.Failures);
        Assert.True(File.Exists(Path.Combine(_root, "good.bin")));
    }
}
