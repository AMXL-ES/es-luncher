using EsLuncher.Core.Downloads;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Metadata;

// 最小启动器示例：不碰 UI 框架，只用 EsLuncher.Core。
// 用法：dotnet run --project samples/MinimalLauncher -- <数据根> <版本号>
// 例：  dotnet run --project samples/MinimalLauncher -- D:\mc-data 1.18.2

var dataRoot = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "esl-minimal");
var versionId = args.Length > 1 ? args[1] : "1.18.2";

var layout = new DataLayout(dataRoot);
using var client = new HttpRemoteFileClient();
var downloader = new FileDownloader(client);
var catalog = new VersionCatalog(layout, downloader);
var fetcher = new VersionFetcher(layout, downloader);

Console.WriteLine($"数据根 {dataRoot} / 版本 {versionId}");

var report = await fetcher.FetchAsync(versionId, new FetchOptions(), null, CancellationToken.None);
Console.WriteLine($"下载 {report.Downloaded}，跳过 {report.Skipped}，失败 {report.Failed}，资源对象 {report.AssetObjects}");

var manifest = await catalog.GetManifestAsync(false, CancellationToken.None);
var version = await catalog.GetVersionAsync(versionId, manifest, false, CancellationToken.None);

var selection = JavaSelector.Select(
    version.JavaVersion?.MajorVersion ?? 8,
    JavaDiscovery.Discover([layout.JavaDir]),
    null);
Console.WriteLine(selection.Message);
if (!selection.IsSatisfied)
{
    return 1;
}

var command = LaunchCommandBuilder.Build(
    version,
    LaunchEnvironment.Detect(),
    layout,
    selection.Runtime!,
    new LaunchRequest { Username = "Dev", MemoryMb = 2048 });

var classpath = command.Classpath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
Console.WriteLine($"java      : {command.JavaExePath}");
Console.WriteLine($"工作目录  : {command.WorkingDirectory}");
Console.WriteLine($"classpath : {classpath.Length} 项");

// 真正启动只需要这一行（示例里故意不启动，避免误开游戏）：
// var result = await ProcessRunner.RunLoggedAsync(command, logPath, null, CancellationToken.None);
Console.WriteLine("示例已组装好命令；要真启动，取消上面 ProcessRunner 那行的注释。");
return 0;
