using Avalonia;

namespace GLauncher.App;

internal static class Program
{
    // Win32 下 Avalonia 需要 STA 线程（COM / 通用控件依赖它）。
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
