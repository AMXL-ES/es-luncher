using System.Security.Cryptography;
using System.Text;
using GLauncher.Core.Downloads;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public sealed class VersionCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly FakeRemoteFileClient _client;
    private readonly VersionCatalog _catalog;

    public VersionCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "glauncher-catalog-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _client = new FakeRemoteFileClient();
        _catalog = new VersionCatalog(_layout, new FileDownloader(_client));
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
    public async Task Fetches_manifest_then_reuses_cache()
    {
        var manifestJson = TestFixtures.Load("manifest.min.json");
        _client.OnGet = (_, _, _) => Task.FromResult(FakeRemoteFileClient.Ok(Encoding.UTF8.GetBytes(manifestJson)));

        var first = await _catalog.GetManifestAsync(refresh: false, CancellationToken.None);
        var second = await _catalog.GetManifestAsync(refresh: false, CancellationToken.None);

        Assert.Equal(5, first.Versions.Count);
        Assert.Equal(5, second.Versions.Count);
        Assert.Single(_client.Requests);
        Assert.True(File.Exists(_layout.ManifestFile));
    }

    [Fact]
    public async Task Fetches_version_json_and_verifies_sha1_from_manifest()
    {
        var versionJson = TestFixtures.Load("1.7.10.min.json");
        var versionSha = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(versionJson)));
        var manifestJson = $$"""
        {"latest":{"release":"1.7.10","snapshot":"1.7.10"},"versions":[
          {"id":"1.7.10","type":"release","url":"https://example.test/1.7.10.json",
           "sha1":"{{versionSha}}","releaseTime":"2014-05-14T17:29:23+00:00"}]}
        """;
        _client.OnGet = (uri, _, _) => Task.FromResult(uri.AbsoluteUri.Contains("version_manifest", StringComparison.Ordinal)
            ? FakeRemoteFileClient.Ok(Encoding.UTF8.GetBytes(manifestJson))
            : FakeRemoteFileClient.Ok(Encoding.UTF8.GetBytes(versionJson)));

        var manifest = await _catalog.GetManifestAsync(refresh: false, CancellationToken.None);
        var version = await _catalog.GetVersionAsync("1.7.10", manifest, refresh: false, CancellationToken.None);
        var cached = await _catalog.GetVersionAsync("1.7.10", manifest, refresh: false, CancellationToken.None);

        Assert.Equal("1.7.10", version.Id);
        Assert.Equal(8, version.JavaVersion!.MajorVersion);
        Assert.Equal("1.7.10", cached.Id);

        // 清单 + 一次 version.json；第二次命中缓存
        Assert.Equal(2, _client.Requests.Count);
        Assert.True(File.Exists(_layout.VersionJson("1.7.10")));
    }

    [Fact]
    public async Task Unknown_version_throws()
    {
        var manifestJson = TestFixtures.Load("manifest.min.json");
        _client.OnGet = (_, _, _) => Task.FromResult(FakeRemoteFileClient.Ok(Encoding.UTF8.GetBytes(manifestJson)));
        var manifest = await _catalog.GetManifestAsync(refresh: false, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _catalog.GetVersionAsync("9.9.9", manifest, refresh: false, CancellationToken.None));
    }
}
