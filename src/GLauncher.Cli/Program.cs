using System.Globalization;
using GLauncher.Core.Auth;
using GLauncher.Core.Downloads;
using GLauncher.Core.Launching;
using GLauncher.Core.Metadata;

namespace GLauncher.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args) => await RunAsync(args).ConfigureAwait(false);

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = args[0];
            var options = CommandOptions.Parse(args[1..]);
            return command.ToLowerInvariant() switch
            {
                "fetch" => await FetchAsync(options).ConfigureAwait(false),
                "launch" => await LaunchAsync(options).ConfigureAwait(false),
                "java" => ListJava(),
                "versions" => await VersionsAsync(options).ConfigureAwait(false),
                "login" => await LoginAsync(options).ConfigureAwait(false),
                "accounts" => ListAccounts(options),
                "logout" => await LogoutAsync(options).ConfigureAwait(false),
                _ => Fail($"未知命令：{command}"),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex) when (ex is MetadataFormatException or InvalidOperationException or IOException or HttpRequestException or TimeoutException)
        {
            return Fail(ex.Message);
        }
    }

    private static int ListJava()
    {
        var runtimes = JavaDiscovery.Discover();
        Console.WriteLine($"发现 {runtimes.Count} 个可用 Java（PATH 上的 java 一律忽略）：");
        foreach (var runtime in runtimes)
        {
            Console.WriteLine($"  Java {runtime.MajorVersion,2}  {runtime.JavaExePath}");
        }

        return runtimes.Count > 0 ? 0 : 1;
    }

    private static async Task<int> VersionsAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var catalog = new VersionCatalog(layout, new FileDownloader(client));
        var manifest = await catalog.GetManifestAsync(options.Refresh, CancellationToken.None).ConfigureAwait(false);

        Console.WriteLine(
            $"manifest: {manifest.Versions.Count} 个版本，latest release={manifest.Latest.Release}，snapshot={manifest.Latest.Snapshot}");

        var type = string.IsNullOrWhiteSpace(options.Type) ? "release" : options.Type!;
        var entries = VersionQuery.Latest(manifest, type, options.Limit);
        Console.WriteLine($"type={type} 共取 {entries.Count} 条（新→旧）：");
        foreach (var entry in entries)
        {
            Console.WriteLine($"  {entry.Id,-20} {entry.Type,-10} {(entry.ReleaseTime?.ToString("yyyy-MM-dd") ?? "-")}");
        }

        return 0;
    }

    private static async Task<int> LoginAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var provider = (options.Provider ?? "offline").ToLowerInvariant();

        if (provider == "offline")
        {
            if (string.IsNullOrWhiteSpace(options.Username))
            {
                return Fail("login offline 需要 --username <名字>");
            }

            var session = await new OfflineAuthProvider(options.Username).LoginAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None)
                .ConfigureAwait(false);
            Console.WriteLine($"已保存离线账号：{session.Describe()}");
            return 0;
        }

        if (provider == "microsoft")
        {
            var clientId = options.ClientId ?? Environment.GetEnvironmentVariable("GLAUNCHER_MSA_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Console.Error.WriteLine("错误：微软登录需要一个 Azure 客户端 ID（公共客户端应用）。");
                Console.Error.WriteLine("  1) portal.azure.com 注册应用（个人账号免费），重定向填 http://localhost");
                Console.Error.WriteLine("  2) 在「身份验证」里允许公共客户端流（设备代码）");
                Console.Error.WriteLine("  3) glauncher login --provider microsoft --client-id <id>，或设置 GLAUNCHER_MSA_CLIENT_ID");
                return 2;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var client = new MicrosoftAuthClient(http, clientId);
            var session = await client.LoginAsync(
                device =>
                {
                    Console.WriteLine();
                    Console.WriteLine($"浏览器打开 {device.VerificationUri}，输入代码：{device.UserCode}");
                    Console.WriteLine(device.Message);
                    Console.WriteLine();
                },
                CancellationToken.None).ConfigureAwait(false);

            await store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None)
                .ConfigureAwait(false);
            Console.WriteLine($"已保存微软账号：{session.Describe()}");
            return 0;
        }

        return Fail($"未知认证方式：{provider}（支持 offline / microsoft）");
    }

    private static int ListAccounts(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var accounts = store.ListAccounts();
        if (accounts.Count == 0)
        {
            Console.WriteLine("没有已保存的账号。用 `glauncher login --provider offline --username <名字>` 添加。");
            return 0;
        }

        foreach (var key in accounts)
        {
            try
            {
                var session = store.LoadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine($"  {key,-16} {session?.Describe() ?? "(无法解密)"}");
            }
            catch (AuthenticationException ex)
            {
                Console.WriteLine($"  {key,-16} 解密失败：{ex.Message}");
            }
        }

        return 0;
    }

    private static async Task<int> LogoutAsync(CommandOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Account))
        {
            return Fail("logout 需要 --account <名字>");
        }

        var layout = new DataLayout(options.DataRoot);
        var store = new DpapiTokenStore(layout);
        var removed = await store.DeleteAsync(options.Account, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(removed ? $"已删除账号 {options.Account}" : $"没有这个账号：{options.Account}");
        return removed ? 0 : 1;
    }

    private static async Task<int> LaunchAsync(CommandOptions options)
    {
        var versionId = options.RequireVersion();
        var layout = new DataLayout(options.DataRoot);
        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var catalog = new VersionCatalog(layout, downloader);

        var manifest = await catalog.GetManifestAsync(options.Refresh, CancellationToken.None).ConfigureAwait(false);
        var version = await catalog.GetVersionAsync(versionId, manifest, options.Refresh, CancellationToken.None)
            .ConfigureAwait(false);

        var environment = LaunchEnvironment.Detect();
        var requiredMajor = version.JavaVersion?.MajorVersion;
        if (requiredMajor is null)
        {
            Console.WriteLine($"警告：{version.Id} 的 version.json 没有 javaVersion，按 Java 8 处理");
            requiredMajor = 8;
        }

        JavaRuntime? overridden = null;
        if (!string.IsNullOrWhiteSpace(options.JavaPath))
        {
            overridden = JavaRuntime.TryProbe(options.JavaPath);
            if (overridden is null)
            {
                return Fail($"--java 指向的不是可用的 java：{options.JavaPath}");
            }
        }

        var selection = JavaSelector.Select(requiredMajor.Value, JavaDiscovery.Discover(), overridden);
        Console.WriteLine(selection.Message);
        if (!selection.IsSatisfied)
        {
            return 3;
        }

        AuthSession? session = null;
        if (!string.IsNullOrWhiteSpace(options.Account))
        {
            var store = new DpapiTokenStore(layout);
            session = await store.LoadAsync(options.Account, CancellationToken.None).ConfigureAwait(false);
            if (session is null)
            {
                return Fail($"没有找到账号 {options.Account}，先 `glauncher login`");
            }

            if (session.IsExpired(DateTimeOffset.UtcNow))
            {
                Console.WriteLine("警告：会话已过期；启动可能被服务端拒绝（M3b 的自动续期尚未接入）");
            }

            Console.WriteLine($"账号：{session.Describe()}");
        }

        var request = new LaunchRequest
        {
            Username = session?.Profile.Name ?? options.Username ?? "Player",
            Uuid = session?.Profile.Id,
            AccessToken = session?.AccessToken,
            UserType = session?.UserType ?? "legacy",
            InstanceId = options.InstanceId,
            MemoryMb = options.MemoryMb,
        };
        var command = LaunchCommandBuilder.Build(version, environment, layout, selection.Runtime!, request);

        var classpathEntries = command.Classpath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var missing = classpathEntries.Where(path => !File.Exists(path)).ToArray();
        var nativesFiles = Directory.Exists(command.NativesDirectory)
            ? Directory.GetFiles(command.NativesDirectory).Length
            : 0;

        Console.WriteLine();
        Console.WriteLine($"version   : {version.Id} ({version.Type})  java required={requiredMajor}");
        Console.WriteLine($"java      : {command.JavaExePath}");
        Console.WriteLine($"working   : {command.WorkingDirectory}");
        Console.WriteLine($"natives   : {command.NativesDirectory} ({nativesFiles} files)");
        Console.WriteLine($"classpath : {classpathEntries.Length} entries, missing {missing.Length}");
        foreach (var path in missing.Take(5))
        {
            Console.WriteLine($"  ! missing {path}");
        }

        Console.WriteLine($"arguments : {command.Arguments.Count}");
        foreach (var argument in command.Arguments.Take(14))
        {
            Console.WriteLine($"  {argument}");
        }

        if (command.Arguments.Count > 14)
        {
            Console.WriteLine("  ...");
        }

        if (!string.IsNullOrWhiteSpace(options.DumpPath))
        {
            File.WriteAllLines(
                options.DumpPath,
                new[] { command.JavaExePath }.Concat(command.Arguments));
            Console.WriteLine($"command dumped: {options.DumpPath}");
        }

        if (options.DryRun)
        {
            Console.WriteLine("dry-run：未启动进程");
            return missing.Length == 0 ? 0 : 4;
        }
        if (missing.Length > 0)
        {
            return Fail($"classpath 缺 {missing.Length} 个文件，先跑 `glauncher fetch --version {version.Id}`");
        }

        Directory.CreateDirectory(command.WorkingDirectory);
        var logPath = Path.Combine(
            layout.LogsDir,
            $"launch-{version.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Console.WriteLine($"log       : {logPath}");
        Console.WriteLine(options.AutoStopSeconds > 0
            ? $"启动游戏（{options.AutoStopSeconds}s 后自动停止，用于验收）…"
            : "启动游戏（等待进程退出）…");

        var autoStop = options.AutoStopSeconds > 0 ? TimeSpan.FromSeconds(options.AutoStopSeconds) : (TimeSpan?)null;
        var result = await ProcessRunner.RunLoggedAsync(command, logPath, autoStop, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine("== launch result ==");
        Console.WriteLine($"autoStopped : {result.AutoStopped}");
        Console.WriteLine($"exit code   : {(result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "(killed)")}");
        Console.WriteLine($"duration    : {result.Duration.TotalSeconds:0.0} s");
        Console.WriteLine($"log         : {result.LogPath}");

        if (result.AutoStopped)
        {
            // 验收判据：起来且撑到时间点 → 成功（不是"崩溃秒退"）
            return 0;
        }

        return result.ExitCode == 0 ? 0 : 1;
    }

    private static async Task<int> FetchAsync(CommandOptions options)
    {
        var versionId = options.RequireVersion();
        var layout = new DataLayout(options.DataRoot);
        Console.WriteLine($"GLauncher CLI · fetch {versionId}");
        Console.WriteLine($"data root : {layout.Root}");
        Console.WriteLine($"concurrency: {options.Concurrency}   selection: {options.Selection.Describe()}");

        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var fetcher = new VersionFetcher(layout, downloader);
        var fetchOptions = new FetchOptions
        {
            Selection = options.Selection,
            Concurrency = options.Concurrency,
            Refresh = options.Refresh,
        };

        if (options.DryRun)
        {
            var (version, plan) = await fetcher.BuildPlanAsync(versionId, fetchOptions, CancellationToken.None)
                .ConfigureAwait(false);
            PrintPlan(version, plan);
            return 0;
        }

        var progress = new ConsoleProgress();
        var report = await fetcher.FetchAsync(versionId, fetchOptions, progress, CancellationToken.None)
            .ConfigureAwait(false);
        PrintReport(report);
        return report.Success ? 0 : 1;
    }

    private static void PrintPlan(VersionMetadata version, DownloadPlan plan)
    {
        var bytes = plan.Files.Sum(job => job.ExpectedSize ?? 0);
        Console.WriteLine();
        Console.WriteLine($"version   : {version.Id} ({version.Type})  java={version.JavaVersion?.MajorVersion.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        Console.WriteLine($"libraries : {version.Libraries.Count} 条（过滤后进计划 {plan.Files.Count(job => job.TargetPath.Contains("libraries", StringComparison.OrdinalIgnoreCase))} 条）");
        Console.WriteLine($"natives   : {plan.Natives.Count} 个解压目标");
        Console.WriteLine($"files     : {plan.Files.Count} 个文件，预计 {FormatBytes(bytes)}");
        Console.WriteLine("first files:");
        foreach (var job in plan.Files.Take(8))
        {
            Console.WriteLine($"  - {job.Primary}");
            Console.WriteLine($"    -> {job.TargetPath}");
        }
    }

    private static void PrintReport(FetchReport report)
    {
        Console.WriteLine();
        Console.WriteLine("== report ==");
        Console.WriteLine($"version.json : {report.VersionJsonPath}");
        Console.WriteLine($"client jar   : {report.ClientJarPath ?? "(skipped)"}");
        Console.WriteLine($"files        : planned={report.FilesPlanned} downloaded={report.Downloaded} skipped={report.Skipped} failed={report.Failed}");
        Console.WriteLine($"bytes        : {FormatBytes(report.BytesDownloaded)}");
        Console.WriteLine($"assets       : {report.AssetObjects} objects");
        Console.WriteLine($"natives      : {report.NativesExtracted} files extracted");

        if (report.Failures.Count > 0)
        {
            Console.WriteLine($"failures     : {report.Failures.Count}");
            foreach (var failure in report.Failures.Take(10))
            {
                Console.WriteLine($"  - {failure.Path}: {failure.Error}");
            }
        }
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.0} MiB"
            : $"{bytes / 1024d:0.0} KiB";

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"错误：{message}");
        Console.Error.WriteLine("用 `glauncher --help` 查看用法。");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        GLauncher CLI（M1 fetch / M2 launch 验收面）

        用法:
          glauncher fetch    --version <id> [选项]
          glauncher launch   --version <id> [选项]
          glauncher java
          glauncher versions [--type <t>] [--limit <n>]
          glauncher login    --provider offline|microsoft [选项]
          glauncher accounts
          glauncher logout   --account <名字>

        选项:
          --version <id>     必填，例如 1.7.10 / 1.12.2 / 26.3
          --data <dir>       数据根（默认 D:\mc-data，或环境变量 GLAUNCHER_DATA）
          --concurrency <n>  fetch 并发下载数（默认 8）
          --only <parts>     fetch: client,libraries,assets,natives 的子集
          --refresh          强制刷新版本清单与 version.json
          --dry-run          fetch: 只算计划；launch: 只打印命令行
          --username <name>  launch 的离线用户名 / login offline 的名字（默认 Player）
          --instance <id>    launch 的实例名（默认同版本号）
          --java <path>      launch 指定 java.exe（默认按 version.json 要求自动选）
          --memory-mb <n>    launch 的 -Xmx（默认 2048）
          --autostop <s>     launch 启动后 s 秒自动停止（验收用；0 = 等待退出）
          --account <name>   launch 用已保存账号；logout 指定账号
          --provider <p>     login: offline | microsoft
          --client-id <id>   login microsoft 的 Azure 客户端 ID（或 GLAUNCHER_MSA_CLIENT_ID）
          --dump <file>      把完整命令行写到文件（排障/复现）
          --type <t>         versions: release|snapshot|old_alpha|old_beta|all（默认 release）
          --limit <n>        versions: 取几条（默认 10）

        示例:
          glauncher fetch  --version 1.7.10 --dry-run
          glauncher login  --provider offline --username Dev
          glauncher launch --version 1.18.2 --account dev --autostop 30
          glauncher login  --provider microsoft --client-id <azure-client-id>
        """);
    }

    private sealed class ConsoleProgress : IProgress<DownloadProgress>
    {
        private readonly object _gate = new();
        private DateTime _nextPrint = DateTime.MinValue;

        public void Report(DownloadProgress value)
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var finished = value.FilesDone == value.FilesTotal;
                if (!finished && now < _nextPrint)
                {
                    return;
                }

                _nextPrint = now.AddSeconds(2);
                Console.WriteLine($"[{value.FilesDone}/{value.FilesTotal}] {FormatBytes(value.BytesDownloaded)}  {Path.GetFileName(value.CurrentPath)}");
            }
        }
    }
}

internal sealed class CommandOptions
{
    public string? Version { get; private set; }

    public string DataRoot { get; private set; } = DefaultDataRoot();

    public int Concurrency { get; private set; } = 8;

    public FetchSelection Selection { get; private set; } = FetchSelection.All;

    public bool Refresh { get; private set; }

    public bool DryRun { get; private set; }

    public string? Username { get; private set; }

    public string? InstanceId { get; private set; }

    public string? JavaPath { get; private set; }

    public int MemoryMb { get; private set; } = 2048;

    public int AutoStopSeconds { get; private set; }

    public string? DumpPath { get; private set; }

    public string? Provider { get; private set; }

    public string? Account { get; private set; }

    public string? ClientId { get; private set; }

    public string? Type { get; private set; }

    public int Limit { get; private set; } = 10;

    /// <summary>fetch / launch 需要版本号，java 不需要 —— 所以校验放在命令里而不是 Parse 里。</summary>
    public string RequireVersion()
    {
        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException("缺少必填参数 --version <id>");
        }

        return Version!;
    }

    public static CommandOptions Parse(string[] args)
    {
        var options = new CommandOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--version":
                    options.Version = Next(args, ref i);
                    break;
                case "--data":
                    options.DataRoot = Next(args, ref i);
                    break;
                case "--concurrency":
                    options.Concurrency = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--only":
                    options.Selection = ParseOnly(Next(args, ref i));
                    break;
                case "--refresh":
                    options.Refresh = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--username":
                    options.Username = Next(args, ref i);
                    break;
                case "--instance":
                    options.InstanceId = Next(args, ref i);
                    break;
                case "--java":
                    options.JavaPath = Next(args, ref i);
                    break;
                case "--memory-mb":
                    options.MemoryMb = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--autostop":
                    options.AutoStopSeconds = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--dump":
                    options.DumpPath = Next(args, ref i);
                    break;
                case "--provider":
                    options.Provider = Next(args, ref i);
                    break;
                case "--account":
                    options.Account = Next(args, ref i);
                    break;
                case "--client-id":
                    options.ClientId = Next(args, ref i);
                    break;
                case "--type":
                    options.Type = Next(args, ref i);
                    break;
                case "--limit":
                    options.Limit = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"未知参数：{args[i]}");
            }
        }

        if (options.Concurrency < 1 || options.Concurrency > 64)
        {
            throw new ArgumentException("--concurrency 必须在 1..64");
        }

        if (options.MemoryMb < 512)
        {
            throw new ArgumentException("--memory-mb 至少 512");
        }

        if (options.AutoStopSeconds < 0)
        {
            throw new ArgumentException("--autostop 不能为负");
        }

        if (options.Limit < 1 || options.Limit > 200)
        {
            throw new ArgumentException("--limit 必须在 1..200");
        }

        return options;
    }

    private static string Next(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
        {
            throw new ArgumentException($"参数 {args[index - 1]} 缺少值");
        }

        return args[index];
    }

    private static FetchSelection ParseOnly(string value)
    {
        var client = false;
        var libraries = false;
        var assets = false;
        var natives = false;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "client": client = true; break;
                case "libraries": libraries = true; break;
                case "assets": assets = true; break;
                case "natives": natives = true; break;
                default: throw new ArgumentException($"--only 不认识的部分：{part}");
            }
        }

        if (!client && !libraries && !assets && !natives)
        {
            throw new ArgumentException("--only 至少要给一个部分");
        }

        return new FetchSelection(client, libraries, assets, natives);
    }

    private static string DefaultDataRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("GLAUNCHER_DATA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        // 宪章：数据不占 C 盘。D 盘存在就用 D，否则退回用户目录。
        return Directory.Exists("D:\\") ? "D:\\mc-data" : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GLauncher");
    }
}

internal static class FetchSelectionExtensions
{
    public static string Describe(this FetchSelection selection)
    {
        var parts = new List<string>(4);
        if (selection.Client) { parts.Add("client"); }
        if (selection.Libraries) { parts.Add("libraries"); }
        if (selection.Assets) { parts.Add("assets"); }
        if (selection.Natives) { parts.Add("natives"); }
        return string.Join(',', parts);
    }
}
