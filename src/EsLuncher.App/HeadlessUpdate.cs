using EsLuncher.Core.Updates;

namespace EsLuncher.App;

/// <summary>
/// 无界面更新路径，给自动验收与排障用（正常用户走界面上的「检查更新」）：
/// <code>EsLuncher.exe --update-test [--source &lt;目录或 URL&gt;] [--log &lt;文件&gt;] [--apply]</code>
/// 输出 <c>key=value</c> 形式，便于脚本断言；退出码 0 = 检查成功，1 = 失败。
/// </summary>
internal static class HeadlessUpdate
{
    private const string Trigger = "--update-test";

    public static int? TryRun(string[] args)
    {
        if (!args.Any(argument => string.Equals(argument, Trigger, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var source = Value(args, "--source");
        var logPath = Value(args, "--log");
        var apply = args.Any(argument => string.Equals(argument, "--apply", StringComparison.OrdinalIgnoreCase));

        void Log(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            Console.WriteLine(line);
            if (string.IsNullOrWhiteSpace(logPath))
            {
                return;
            }

            try
            {
                var full = Path.GetFullPath(logPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.AppendAllText(full, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // 日志写不进去不该让更新本身失败。
            }
        }

        try
        {
            var service = new VelopackUpdateService(source, Log);
            var result = service.CheckAsync().GetAwaiter().GetResult();
            Console.WriteLine($"installed={result.IsInstalled}");
            Console.WriteLine($"portable={result.IsPortable}");
            Console.WriteLine($"current={result.CurrentVersion}");
            Console.WriteLine($"available={result.AvailableVersion ?? "none"}");
            Console.WriteLine($"delta={result.DeltaAvailable}");

            if (!result.UpdateAvailable)
            {
                Console.WriteLine("downloaded=no");
                return 0;
            }

            service.DownloadAsync(percent => Log($"下载进度 {percent}%")).GetAwaiter().GetResult();
            Console.WriteLine("downloaded=yes");

            if (apply)
            {
                Console.WriteLine("applying=yes");
                service.ApplyAndRestart();
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"更新失败：{exception.Message}");
            Log($"更新失败：{exception.Message}");
            return 1;
        }
    }

    private static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
