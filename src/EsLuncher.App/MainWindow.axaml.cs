using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class MainWindow : Window
{
    private readonly DataLayout _layout;
    private readonly InstanceStore _store;
    private readonly LaunchService _launcher;

    public MainWindow()
    {
        InitializeComponent();

        _layout = AppServices.Layout;
        _store = new InstanceStore(_layout);
        _launcher = new LaunchService(_layout);

        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EsLuncher/Assets/es-luncher-256.png")));
        StudioLogo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://EsLuncher/Assets/endless-studio-logo.jpg")));

        RefreshInstances();

        // 开发/验收用的页面起始入口：ESLUNCHER_UI_PAGE=downloads|settings|about
        var page = Environment.GetEnvironmentVariable("ESLUNCHER_UI_PAGE")?.ToLowerInvariant();
        NavList.SelectedIndex = 0;
        ApplyNav("instances");
        if (page is "downloads" or "settings" or "about")
        {
            var index = page switch
            {
                "downloads" => 1,
                "settings" => 2,
                _ => 3,
            };
            NavList.SelectedIndex = index;
            ApplyNav(page);
        }
    }

    private void ApplyNav(string tag)
    {
        if (InstancesPage is null || DownloadsPage is null || SettingsPage is null || AboutPage is null)
        {
            return;
        }

        InstancesPage.IsVisible = tag == "instances";
        DownloadsPage.IsVisible = tag == "downloads";
        SettingsPage.IsVisible = tag == "settings";
        AboutPage.IsVisible = tag == "about";
    }

    private void RefreshInstances()
    {
        var items = _store.List().Select(config => new InstanceItem(config)).ToArray();
        InstanceList.ItemsSource = items;
        if (items.Length > 0)
        {
            InstanceList.SelectedIndex = 0;
        }

        StatusText.Text = items.Length == 0
            ? "还没有实例。用 CLI 创建：esluncher instance create <名字> --version <id>"
            : $"共 {items.Length} 个实例";
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => RefreshInstances();

    private async void OnLaunchClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            StatusText.Text = "先选一个实例";
            return;
        }

        try
        {
            var outcome = await _launcher.LaunchAsync(
                item.Config,
                message => Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText.Text = message),
                CancellationToken.None);
            StatusText.Text = outcome.Message;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"启动失败：{exception.Message}";
        }
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", _layout.InstanceDir(item.Config.Name))
        {
            UseShellExecute = true,
        });
    }

    private void OnNavChanged(object? sender, SelectionChangedEventArgs e)
    {
        // XAML 加载期间 IsSelected 会先触发一次，此时命名控件还没赋值。
        if (NavList is null)
        {
            return;
        }

        var tag = (NavList.SelectedItem as ListBoxItem)?.Tag as string ?? "instances";
        ApplyNav(tag);
    }
}

/// <summary>实例列表的展示项：只暴露界面需要的字段。</summary>
public sealed class InstanceItem(InstanceConfig config)
{
    public InstanceConfig Config { get; } = config;

    public string Name => Config.Name;

    public string Version => Config.VersionId;

    public string Detail => $"{Config.MemoryMb} MB · {Config.Username ?? "Player"} · {Config.CreatedAt:yyyy-MM-dd}";
}
