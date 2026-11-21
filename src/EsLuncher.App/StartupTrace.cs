using System.Diagnostics;

namespace EsLuncher.App;

/// <summary>
/// 启动耗时埋点。只在 <c>ESLUNCHER_STARTUP_TRACE=1</c> 时生效，写到 <c>%TEMP%\esluncher-startup.log</c>。
/// 目的：优化前先量，不要凭感觉改。
/// </summary>
public static class StartupTrace
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("ESLUNCHER_STARTUP_TRACE"), "1", StringComparison.Ordinal);

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "esluncher-startup.log");

    public static void Reset()
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            File.WriteAllText(LogPath, string.Empty);
        }
        catch (IOException)
        {
            // 埋点失败不影响启动。
        }
    }

    public static void Mark(string stage)
    {
        if (!Enabled)
        {
            return;
        }

        var line = $"{Clock.ElapsedMilliseconds,6} ms  {stage}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // 同上。
        }
    }
}
