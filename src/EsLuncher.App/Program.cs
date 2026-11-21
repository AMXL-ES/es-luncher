using Avalonia;
using Velopack;

namespace EsLuncher.App;

internal static class Program
{
    // Win32 下 Avalonia 需要 STA 线程（COM / 通用控件依赖它）。
    [STAThread]
    public static int Main(string[] args)
    {
        StartupTrace.Reset();
        StartupTrace.Mark("process start");

        // Velopack 必须最先接管：安装、更新、卸载时它会带参数回调本程序，
        // 这些回调只做文件搬运，不能先初始化 UI。
        VelopackApp.Build().Run();
        StartupTrace.Mark("velopack hooks done");

        // 无界面更新路径（自动验收/排障用）：没带该参数时返回 null。
        if (HeadlessUpdate.TryRun(args) is int headlessExitCode)
        {
            return headlessExitCode;
        }

        var builder = BuildAvaloniaApp();
        StartupTrace.Mark("app builder ready");
        builder.StartWithClassicDesktopLifetime(args);
        StartupTrace.Mark("lifetime exited");
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
