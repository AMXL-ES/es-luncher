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

        // 开发/验收用：ESLUNCHER_UI_CONTENT_KIND=mods|resourcepacks|shaderpacks|worlds|datapacks 选内容类型。
        var contentKind = Environment.GetEnvironmentVariable("ESLUNCHER_UI_CONTENT_KIND")?.ToLowerInvariant();
        if (contentKind is "mods" or "resourcepacks" or "shaderpacks" or "worlds" or "datapacks")
        {
            ContentKindBox.SelectedIndex = contentKind switch
            {
                "resourcepacks" => 1,
                "shaderpacks" => 2,
                "worlds" => 3,
                "datapacks" => 4,
                _ => 0,
            };
            RefreshWorlds();
            RefreshContent();
        }

        // 开发/验收用：ESLUNCHER_UI_DIALOG=diagnostics|modrinth 自动打开弹窗（截图用）。
        var dialog = Environment.GetEnvironmentVariable("ESLUNCHER_UI_DIALOG")?.ToLowerInvariant();
        if (dialog is "diagnostics" or "modrinth" && InstanceList.SelectedItem is InstanceItem selected)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (dialog == "diagnostics")
                {
                    await new DiagnosticsWindow(selected.Config.Name, _layout).ShowDialog(this);
                }
                else
                {
                    var (minecraftVersion, loader) = InstanceVersionInfo.Derive(selected.Config.VersionId);
                    var modrinthLoader = loader == "fabric-loader" ? "fabric" : loader == "quilt-loader" ? "quilt" : loader;
                    await new ModrinthWindow(selected.Config.Name, _layout, minecraftVersion, modrinthLoader).ShowDialog(this);
                }
            });
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

    private InstanceItem[] _allInstances = [];

    private void RefreshInstances()
    {
        _allInstances = _store.List().Select(config => new InstanceItem(config)).ToArray();
        ApplyInstanceFilter();
    }

    private void ApplyInstanceFilter()
    {
        // XAML 加载期 TextChanged 会先触发一次，此时命名控件可能还没赋值。
        if (InstanceList is null || StatusText is null)
        {
            return;
        }

        var query = SearchBox?.Text?.Trim();
        var items = string.IsNullOrWhiteSpace(query)
            ? _allInstances
            : _allInstances
                .Where(item => item.Name.Contains(query!, StringComparison.OrdinalIgnoreCase)
                    || item.Version.Contains(query!, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        InstanceList.ItemsSource = items;
        if (items.Length > 0)
        {
            InstanceList.SelectedIndex = 0;
        }

        StatusText.Text = items.Length == 0
            ? "没有匹配的实例"
            : $"共 {items.Length} 个实例" + (string.IsNullOrWhiteSpace(query) ? string.Empty : $"（筛选自 {_allInstances.Length}）");
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyInstanceFilter();

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => RefreshInstances();

    private ContentKind CurrentContentKind => ContentKindBox.SelectedIndex switch
    {
        1 => ContentKind.ResourcePacks,
        2 => ContentKind.ShaderPacks,
        3 => ContentKind.Worlds,
        4 => ContentKind.Datapacks,
        5 => ContentKind.Screenshots,
        _ => ContentKind.Mods,
    };

    private string? SelectedWorld => WorldBox.SelectedItem as string;

    private void OnInstanceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RefreshWorlds();
        RefreshContent();
    }

    private void OnContentKindChanged(object? sender, SelectionChangedEventArgs e) => RefreshContent();

    private void OnWorldChanged(object? sender, SelectionChangedEventArgs e) => RefreshContent();

    private void RefreshWorlds()
    {
        if (WorldBox is null || InstanceList?.SelectedItem is not InstanceItem item)
        {
            return;
        }

        var service = new InstanceContentService(_layout, _store);
        var worlds = service.List(item.Config.Name, ContentKind.Worlds).Select(entry => entry.Name).ToArray();
        WorldBox.ItemsSource = worlds;
        if (worlds.Length > 0)
        {
            WorldBox.SelectedIndex = 0;
        }
    }

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
        WorldBox.IsVisible = kind == ContentKind.Datapacks;
        CheckUpdatesButton.IsVisible = kind == ContentKind.Mods;
        BackupWorldButton.IsVisible = kind == ContentKind.Worlds;
        if (kind == ContentKind.Datapacks && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            ContentList.ItemsSource = null;
            ContentStatus.Text = "这个实例还没有世界；先进入游戏创建一个，或添加世界目录";
            return;
        }

        var service = new InstanceContentService(_layout, _store);
        var files = service.List(item.Config.Name, kind, SelectedWorld);
        ContentList.ItemsSource = files
            .Select(file => new ContentItem(
                file,
                kind == ContentKind.Mods ? DescribeMod(Path.Combine(service.DirectoryFor(item.Config.Name, kind), file.Name)) : null))
            .ToArray();
        ContentTitle.Text = $"内容 · {item.Config.Name}";
        ContentStatus.Text = files.Count == 0
            ? $"空（{InstanceContentService.FolderName(kind)}）"
            : $"{files.Count} 个 · 目录 {service.DirectoryFor(item.Config.Name, kind, SelectedWorld)}";
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

        var kind = CurrentContentKind;
        try
        {
            string sourcePath;
            if (kind == ContentKind.Worlds)
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "选择世界目录（含 level.dat）",
                    AllowMultiple = false,
                });
                if (folders.Count == 0)
                {
                    return;
                }

                sourcePath = folders[0].Path.LocalPath;
            }
            else
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择要添加的文件",
                    AllowMultiple = false,
                });
                if (files.Count == 0)
                {
                    return;
                }

                sourcePath = files[0].Path.LocalPath;
            }

            var added = new InstanceContentService(_layout, _store)
                .Add(item.Config.Name, kind, sourcePath, SelectedWorld);
            ContentStatus.Text = $"已添加 {added.Name}";
            RefreshWorlds();
            RefreshContent();
        }
        catch (Exception exception)
        {
            ContentStatus.Text = $"添加失败：{exception.Message}";
        }
    }

    private void OnToggleContentClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item || ContentList.SelectedItem is not ContentItem content)
        {
            ContentStatus.Text = "先选中要启用/禁用的内容";
            return;
        }

        try
        {
            var service = new InstanceContentService(_layout, _store);
            var changed = service.SetEnabled(item.Config.Name, CurrentContentKind, content.File.Name, !content.File.Enabled, SelectedWorld);
            ContentStatus.Text = changed
                ? $"{(content.File.Enabled ? "已禁用" : "已启用")} {content.File.Name}"
                : "操作失败：内容不存在";
            RefreshContent();
        }
        catch (NotSupportedException exception)
        {
            ContentStatus.Text = exception.Message;
        }
    }

    private async void OnCheckUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            ContentStatus.Text = "先选一个实例";
            return;
        }

        try
        {
            ContentStatus.Text = "检查更新中（Modrinth）…";
            var (minecraftVersion, loader) = InstanceVersionInfo.Derive(item.Config.VersionId);
            var modrinthLoader = loader == "fabric-loader" ? "fabric" : loader == "quilt-loader" ? "quilt" : loader;
            using var client = new HttpRemoteFileClient();
            var updater = new ModUpdater(new InstanceContentService(_layout, _store), new ModrinthClient(client));
            var statuses = await updater.CheckAsync(item.Config.Name, minecraftVersion, modrinthLoader, CancellationToken.None);
            var updatable = statuses.Count(status => status.UpdateAvailable);
            ContentStatus.Text = updatable == 0
                ? "模组都是最新（静态检查）"
                : $"{updatable} 个模组可更新（CLI：esluncher content update --instance \"{item.Config.Name}\"）";
        }
        catch (Exception exception)
        {
            ContentStatus.Text = $"检查更新失败：{exception.Message}";
        }
    }

    private async void OnBackupWorldClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item || ContentList.SelectedItem is not ContentItem content)
        {
            ContentStatus.Text = "先选中一个世界";
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "备份世界",
            SuggestedFileName = $"{content.File.Name}-backup.zip",
            DefaultExtension = "zip",
        });
        if (file is null)
        {
            return;
        }

        try
        {
            var path = new WorldManager(_layout, _store).Backup(item.Config.Name, content.File.Name, file.Path.LocalPath);
            ContentStatus.Text = $"已备份 → {path}";
        }
        catch (Exception exception)
        {
            ContentStatus.Text = $"备份失败：{exception.Message}";
        }
    }

    private async void OnDownloadContentClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            ContentStatus.Text = "先选一个实例";
            return;
        }

        var kind = CurrentContentKind;
        if (kind == ContentKind.Worlds)
        {
            ContentStatus.Text = "世界不支持从 Modrinth 下载";
            return;
        }

        if (kind == ContentKind.Datapacks && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            ContentStatus.Text = "先选一个世界，再装数据包";
            return;
        }

        var (minecraftVersion, loader) = InstanceVersionInfo.Derive(item.Config.VersionId);
        var modrinthLoader = loader switch
        {
            "fabric-loader" => "fabric",
            "quilt-loader" => "quilt",
            var other => other,
        };
        await new ModrinthWindow(item.Config.Name, _layout, minecraftVersion, modrinthLoader, kind, SelectedWorld)
            .ShowDialog(this);
        RefreshContent();
    }

    private void OnRemoveContentClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item || ContentList.SelectedItem is not ContentItem content)
        {
            ContentStatus.Text = "先选中要移除的文件";
            return;
        }

        var removed = new InstanceContentService(_layout, _store)
            .Remove(item.Config.Name, CurrentContentKind, content.File.Name, SelectedWorld);
        ContentStatus.Text = removed ? $"已移除 {content.File.Name}（在 trash/content 可找回）" : "文件不存在";
        RefreshContent();
    }

    private void OnOpenContentFolderClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            return;
        }

        var directory = new InstanceContentService(_layout, _store).DirectoryFor(item.Config.Name, CurrentContentKind, SelectedWorld);
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

    private async void OnDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            StatusText.Text = "先选一个实例";
            return;
        }

        await new DiagnosticsWindow(item.Config.Name, _layout).ShowDialog(this);
    }

    private async void OnModrinthClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            StatusText.Text = "先选一个实例";
            return;
        }

        var (minecraftVersion, loader) = InstanceVersionInfo.Derive(item.Config.VersionId);
        var modrinthLoader = loader switch
        {
            "fabric-loader" => "fabric",
            "quilt-loader" => "quilt",
            var other => other,
        };
        await new ModrinthWindow(item.Config.Name, _layout, minecraftVersion, modrinthLoader, CurrentContentKind, SelectedWorld)
            .ShowDialog(this);
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
        => (File.Enabled ? string.Empty : "[已禁用] ")
           + (File.IsDirectory ? "<目录> " : string.Empty)
           + (metadata is { Length: > 0 } ? metadata + " · " : string.Empty)
           + (File.Size < 1024 ? $"{File.Size} B" : $"{File.Size / 1024.0:F0} KiB");
}
