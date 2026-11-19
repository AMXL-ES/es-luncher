using System.Globalization;
using GLauncher.Core.Downloads;
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
                _ => Fail($"未知命令：{command}"),
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message);
        }
    }

    private static async Task<int> FetchAsync(CommandOptions options)
    {
        var layout = new DataLayout(options.DataRoot);
        Console.WriteLine($"GLauncher CLI · fetch {options.Version}");
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
            var (version, plan) = await fetcher.BuildPlanAsync(options.Version!, fetchOptions, CancellationToken.None)
                .ConfigureAwait(false);
            PrintPlan(version, plan);
            return 0;
        }

        var progress = new ConsoleProgress();
        var report = await fetcher.FetchAsync(options.Version!, fetchOptions, progress, CancellationToken.None)
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
        GLauncher CLI（M1 验收面）

        用法:
          glauncher fetch --version <id> [选项]

        选项:
          --version <id>     必填，例如 1.7.10 / 1.12.2 / 26.3
          --data <dir>       数据根（默认 D:\mc-data，或环境变量 GLAUNCHER_DATA）
          --concurrency <n>  并发下载数（默认 8）
          --only <parts>     client,libraries,assets,natives 的子集
          --refresh          强制刷新版本清单与 version.json
          --dry-run          只算计划、不下载

        示例:
          glauncher fetch --version 1.7.10 --dry-run
          glauncher fetch --version 1.7.10 --only client,libraries
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
                default:
                    throw new ArgumentException($"未知参数：{args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Version))
        {
            throw new ArgumentException("缺少必填参数 --version <id>");
        }

        if (options.Concurrency < 1 || options.Concurrency > 64)
        {
            throw new ArgumentException("--concurrency 必须在 1..64");
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
