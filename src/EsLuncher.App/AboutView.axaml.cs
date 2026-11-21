using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EsLuncher.App;

/// <summary>
/// 关于页。整页在第一次打开时创建，数据和 352 KB 的工作室图标也一起懒加载，
/// 不让启动路径为「用户可能永远不点」的页面买单。
/// </summary>
public partial class AboutView : UserControl
{
    private bool _loaded;

    public AboutView() => InitializeComponent();

    public void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EsLuncher/Assets/endless-studio-logo.jpg"));
            StudioLogo.Source = Bitmap.DecodeToWidth(stream, 176, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception)
        {
            // 图标缺失不影响关于页其余信息。
        }

        LoadCredits();
        ComponentsList.ItemsSource = AboutInfo.BuildComponents();
        ServicesList.ItemsSource = AboutInfo.Services;

        var version = typeof(AboutView).Assembly.GetName().Version?.ToString(3) ?? "?";
        AboutVersionText.Text = Localization.F("about.versionLine", version);
        EnvRuntimeText.Text = AboutInfo.RuntimeInfo();
        EnvOsText.Text = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        EnvDataText.Text = AppServices.Layout.Root;
        EnvInstallText.Text = Localization.T(IsInstalled() ? "about.env.installInstalled" : "about.env.installDev");
        AboutBuildText.Text = Localization.F("about.buildLine", version, Localization.Language);
    }

    private static bool IsInstalled()
        => Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ESLuncher", "current"));

    private void LoadCredits()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EsLuncher/Assets/credits.json"));
            using var document = JsonDocument.Parse(stream);
            var credits = document.RootElement.GetProperty("credits")
                .EnumerateArray()
                .Select(entry => new CreditItem(
                    entry.GetProperty("name").GetString() ?? string.Empty,
                    entry.TryGetProperty("role", out var role) ? role.GetString() ?? string.Empty : string.Empty))
                .ToArray();
            CreditsList.ItemsSource = credits;
        }
        catch (Exception exception) when (exception is IOException or JsonException or KeyNotFoundException)
        {
            CreditsList.ItemsSource = new[] { new CreditItem("(credits.json 读取失败)", string.Empty) };
        }
    }
}
