using System.Security.Cryptography;
using System.Text;
using GLauncher.Core.Downloads;
using GLauncher.Core.Java;
using GLauncher.Core.Launching;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public class JavaRuntimeCatalogTests
{
    private const string CatalogJson = """
    {"windows-x64":{
       "jre-legacy":[{"manifest":{"sha1":"03828bb8455f3cef94869286a0d941dd16933239","size":80031,"url":"https://piston-meta.mojang.com/v1/packages/03828bb8455f3cef94869286a0d941dd16933239/manifest.json"},"version":{"name":"8u51-cacert462b08","released":"2025-10-06T13:49:59+00:00"}}],
       "java-runtime-gamma":[{"manifest":{"sha1":"abc123","size":1,"url":"https://x/manifest.json"},"version":{"name":"17.0.8","released":"2023-01-01T00:00:00+00:00"}}]
    }}
    """;

    [Fact]
    public void Parses_platform_components()
    {
        var packages = JavaRuntimeCatalog.Parse(CatalogJson, "windows-x64");

        Assert.Equal(2, packages.Count);
        var legacy = JavaRuntimeCatalog.Find(packages, "jre-legacy");
        Assert.NotNull(legacy);
        Assert.Equal("8u51-cacert462b08", legacy!.VersionName);
        Assert.Equal("03828bb8455f3cef94869286a0d941dd16933239", legacy.ManifestSha1);
    }

    [Fact]
    public void Unknown_platform_reports_path()
    {
        var exception = Assert.Throws<MetadataFormatException>(() => JavaRuntimeCatalog.Parse(CatalogJson, "windows-arm64"));

        Assert.Contains("windows-arm64", exception.Path);
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        var packages = JavaRuntimeCatalog.Parse(CatalogJson, "windows-x64");

        Assert.NotNull(JavaRuntimeCatalog.Find(packages, "JRE-LEGACY"));
        Assert.Null(JavaRuntimeCatalog.Find(packages, "nope"));
    }

    [Fact]
    public void Platform_detection_names_are_known_keys()
    {
        var platform = JavaPlatform.Detect();

        Assert.Contains(platform, new[]
        {
            "windows-x64", "windows-x86", "windows-arm64",
            "linux", "linux-i386", "mac-os", "mac-os-arm64",
        });
    }
}

public class JavaRuntimeManifestTests
{
    private const string ManifestJson = """
    {"files":{
       "bin":{"type":"directory"},
       "bin/java.exe":{"type":"file","executable":true,
         "downloads":{"raw":{"sha1":"920752e4c22e1165e6df27f69599483187edfbb3","size":206944,"url":"https://piston-data.mojang.com/v1/objects/920752e4c22e1165e6df27f69599483187edfbb3/javaw.exe"}}},
       "lib/modules":{"type":"file",
         "downloads":{"raw":{"sha1":"03828bb8455f3cef94869286a0d941dd16933239","size":10,"url":"https://x/modules"}}}
    }}
    """;

    [Fact]
    public void Parses_files_and_directories()
    {
        var files = JavaRuntimeManifestParser.Parse(ManifestJson);

        Assert.Equal(3, files.Count);
        Assert.Equal(RuntimeEntryType.Directory, files.Single(file => file.Path == "bin").Type);
        var javaExe = files.Single(file => file.Path == "bin/java.exe");
        Assert.True(javaExe.Executable);
        Assert.Equal(206944, javaExe.Size);
        Assert.Equal("920752e4c22e1165e6df27f69599483187edfbb3", javaExe.Sha1);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("/abs/path")]
    [InlineData("C:/windows/system32")]
    public void Rejects_unsafe_paths(string path)
    {
        const string template = """
        {"files":{"__PATH__":{"type":"file","downloads":{"raw":{"sha1":"00","size":1,"url":"https://x/y"}}}}}
        """;
        var json = template.Replace("__PATH__", path);

        Assert.Throws<MetadataFormatException>(() => JavaRuntimeManifestParser.Parse(json));
    }

    [Fact]
    public void Resolve_target_rejects_traversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "gl-java-root");

        Assert.Throws<MetadataFormatException>(() => JavaRuntimeInstaller.ResolveTarget(root, "../escape"));
        Assert.StartsWith(
            Path.GetFullPath(root),
            JavaRuntimeInstaller.ResolveTarget(root, "bin/java.exe"),
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class JavaRuntimeInstallerTests : IDisposable
{
    private const string ManifestTemplate = """
    {"files":{
       "bin":{"type":"directory"},
       "bin/java.exe":{"type":"file","executable":true,
         "downloads":{"raw":{"sha1":"__EXE_SHA1__","size":__EXE_SIZE__,"url":"https://fake.test/bin/java.exe"}}},
       "lib/a.jar":{"type":"file",
         "downloads":{"raw":{"sha1":"__LIB_SHA1__","size":__LIB_SIZE__,"url":"https://fake.test/lib/a.jar"}}}
    }}
    """;

    private readonly string _root;
    private readonly DataLayout _layout;
    private readonly FakeRemoteFileClient _client;

    public JavaRuntimeInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "glauncher-java-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
        _client = new FakeRemoteFileClient();
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
    public async Task Installs_files_and_returns_probed_runtime()
    {
        var manifestUrl = new Uri("https://fake.test/manifest.json");
        var exeBytes = "fake-java-exe"u8.ToArray();
        var libBytes = "fake-lib"u8.ToArray();
        var (manifestBytes, package) = BuildPackage(manifestUrl, exeBytes, libBytes);

        _client.OnGet = (uri, _, _) =>
        {
            if (uri == manifestUrl)
            {
                return Task.FromResult(FakeRemoteFileClient.Ok(manifestBytes));
            }

            return Task.FromResult(uri.AbsolutePath.EndsWith("java.exe", StringComparison.Ordinal)
                ? FakeRemoteFileClient.Ok(exeBytes)
                : FakeRemoteFileClient.Ok(libBytes));
        };

        var installer = new JavaRuntimeInstaller(
            _layout,
            new FileDownloader(_client),
            path => new JavaRuntime(path, 8, "stub"));
        var runtime = await installer.InstallAsync(package, concurrency: 2, progress: null, CancellationToken.None);

        var installDir = Path.Combine(_layout.JavaDir, "jre-legacy", "8u-test");
        Assert.True(File.Exists(Path.Combine(installDir, "bin", "java.exe")));
        Assert.True(File.Exists(Path.Combine(installDir, "lib", "a.jar")));
        Assert.True(File.Exists(Path.Combine(installDir, ".runtime-manifest.json")));
        Assert.Equal(8, runtime.MajorVersion);
        Assert.Equal(Path.Combine(installDir, "bin", "java.exe"), runtime.JavaExePath);
    }

    [Fact]
    public async Task Second_install_skips_existing_files()
    {
        var manifestUrl = new Uri("https://fake.test/manifest.json");
        var exeBytes = "fake-java-exe"u8.ToArray();
        var libBytes = "fake-lib"u8.ToArray();
        var (manifestBytes, package) = BuildPackage(manifestUrl, exeBytes, libBytes);
        var requests = 0;
        _client.OnGet = (uri, _, _) =>
        {
            Interlocked.Increment(ref requests);
            if (uri == manifestUrl)
            {
                return Task.FromResult(FakeRemoteFileClient.Ok(manifestBytes));
            }

            return Task.FromResult(uri.AbsolutePath.EndsWith("java.exe", StringComparison.Ordinal)
                ? FakeRemoteFileClient.Ok(exeBytes)
                : FakeRemoteFileClient.Ok(libBytes));
        };

        var installer = new JavaRuntimeInstaller(_layout, new FileDownloader(_client), path => new JavaRuntime(path, 8, "stub"));
        await installer.InstallAsync(package, 1, null, CancellationToken.None);
        var afterFirst = requests;
        await installer.InstallAsync(package, 1, null, CancellationToken.None);

        Assert.Equal(afterFirst, requests); // 第二次全部命中缓存，不再请求
    }

    private static (byte[] ManifestBytes, JavaRuntimePackage Package) BuildPackage(Uri manifestUrl, byte[] exeBytes, byte[] libBytes)
    {
        var manifest = ManifestTemplate
            .Replace("__EXE_SHA1__", Sha1(exeBytes))
            .Replace("__EXE_SIZE__", exeBytes.Length.ToString())
            .Replace("__LIB_SHA1__", Sha1(libBytes))
            .Replace("__LIB_SIZE__", libBytes.Length.ToString());
        var manifestBytes = Encoding.UTF8.GetBytes(manifest);
        return (manifestBytes, new JavaRuntimePackage("jre-legacy", "8u-test", manifestUrl, Sha1(manifestBytes)));
    }

    private static string Sha1(byte[] content) => Convert.ToHexStringLower(SHA1.HashData(content));
}
