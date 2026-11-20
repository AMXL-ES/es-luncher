using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using EsLuncher.Core.Content;
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

        // 开发/验收用：ESLUNCHER_UI_INSTANCE=<实例名> 直接选中该实例。
        var instanceName = Environment.GetEnvironmentVariable("ESLUNCHER_UI_INSTANCE");
        if (!string.IsNullOrWhiteSpace(instanceName) && InstanceList.ItemsSource is IEnumerable<InstanceItem> items)
        {
            var match = items.FirstOrDefault(item => string.Equals(item.Name, instanceName, StringComparison.Ordinal));
            if (match is not null)
            {
                InstanceList.SelectedItem = match;
            }
        }

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

    private ContentKind CurrentContentKind => ContentKindBox.SelectedIndex switch
    {
        1 => ContentKind.ResourcePacks,
        2 => ContentKind.ShaderPacks,
        _ => ContentKind.Mods,
    };

    private void OnInstanceSelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshContent();

    private void OnContentKindChanged(object? sender, SelectionChangedEventArgs e) => RefreshContent();

    private void RefreshContent()
    {
        // XAML 加载期间 SelectedIndex 会先触发，此时命名控件可能还没赋值。
        if (ContentStatus is null || ContentList is null || InstanceList is null || ContentTitle is null)
        {
            return;
        }

        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            ContentStatus.Text = "选一个实例";
            ContentList.ItemsSource = null;
            return;
        }

        var kind = CurrentContentKind;
        var service = new InstanceContentService(_layout, _store);
        var files = service.List(item.Config.Name, kind);
        ContentList.ItemsSource = files
            .Select(file => new ContentItem(
                file,
                kind == ContentKind.Mods ? DescribeMod(Path.Combine(service.DirectoryFor(item.Config.Name, kind), file.Name)) : null))
            .ToArray();
        ContentTitle.Text = $"内容 · {item.Config.Name}";
        ContentStatus.Text = files.Count == 0
            ? "空（点「添加」选文件）"
            : $"{files.Count} 个文件 · 目录 {service.DirectoryFor(item.Config.Name, kind)}";
    }

    private static string? DescribeMod(string filePath)
    {
        var metadata = ModMetadataReader.Read(filePath);
        if (metadata.Loader == "unknown")
        {
            return null;
        }

        return $"{metadata.Name ?? metadata.ModId} · {metadata.Version} · {metadata.Loader}";
    }

    private async void OnAddContentClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            ContentStatus.Text = "先选一个实例";
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要添加的文件",
            AllowMultiple = false,
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var added = new InstanceContentService(_layout, _store)
                .Add(item.Config.Name, CurrentContentKind, files[0].Path.LocalPath);
            ContentStatus.Text = $"已添加 {added.Name}";
            RefreshContent();
        }
        catch (Exception exception)
        {
            ContentStatus.Text = $"添加失败：{exception.Message}";
        }
    }

    private void OnRemoveContentClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item || ContentList.SelectedItem is not ContentItem content)
        {
            ContentStatus.Text = "先选中要移除的文件";
            return;
        }

        var removed = new InstanceContentService(_layout, _store)
            .Remove(item.Config.Name, CurrentContentKind, content.File.Name);
        ContentStatus.Text = removed ? $"已移除 {content.File.Name}（在 trash/content 可找回）" : "文件不存在";
        RefreshContent();
    }

    private void OnOpenContentFolderClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            return;
        }

        var directory = new InstanceContentService(_layout, _store).DirectoryFor(item.Config.Name, CurrentContentKind);
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

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

/// <summary>内容列表的展示项。</summary>
public sealed class ContentItem(ContentFile file, string? metadata)
{
    public ContentFile File { get; } = file;

    public string Name => File.Name;

    public string Detail
        => (metadata is { Length: > 0 } ? metadata + " · " : string.Empty)
           + (File.Size < 1024 ? $"{File.Size} B" : $"{File.Size / 1024.0:F0} KiB");
}
