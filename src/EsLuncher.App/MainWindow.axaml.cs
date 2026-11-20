using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using EsLuncher.Core.Content;
using EsLuncher.Core.Auth;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class MainWindow : Window
{
    private readonly DataLayout _layout;
    private readonly InstanceStore _store;
    private readonly LaunchService _launcher;
    private readonly DpapiTokenStore _accounts;

    public MainWindow()
    {
        InitializeComponent();

        _layout = AppServices.Layout;
        _store = new InstanceStore(_layout);
        _launcher = new LaunchService(_layout);
        _accounts = new DpapiTokenStore(_layout);

        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EsLuncher/Assets/es-luncher-256.png")));
        HeaderLogo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://EsLuncher/Assets/es-luncher-256.png")));
        StudioLogo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://EsLuncher/Assets/endless-studio-logo.jpg")));
        LoadCredits();
        Localization.Language = AppServices.Settings.Language;
        ApplyLanguage();

        RefreshInstances();
        ApplyNav("home");

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

        // 开发/验收用的页面起始入口：ESLUNCHER_UI_PAGE=home|instances|downloads|settings|about
        var page = Environment.GetEnvironmentVariable("ESLUNCHER_UI_PAGE")?.ToLowerInvariant();
        if (page is "home" or "instances" or "downloads" or "others" or "settings" or "about")
        {
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

        // 开发/验收用：ESLUNCHER_UI_HOME_POPUP=1 直接展开主页实例选择弹层。
        if (Environment.GetEnvironmentVariable("ESLUNCHER_UI_HOME_POPUP") == "1")
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => InstancePopup.IsOpen = true);
        }

        // 开发/验收用：ESLUNCHER_UI_DIALOG=diagnostics|modrinth 自动打开弹窗（截图用）。
        var dialog = Environment.GetEnvironmentVariable("ESLUNCHER_UI_DIALOG")?.ToLowerInvariant();
        if (dialog is "diagnostics" or "modrinth" or "accounts" && InstanceList.SelectedItem is InstanceItem selected)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (dialog == "diagnostics")
                {
                    await new DiagnosticsWindow(selected.Config.Name, _layout).ShowDialog(this);
                }
                else if (dialog == "accounts")
                {
                    await new AccountsWindow(_layout, selected.Config).ShowDialog(this);
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

        HomePage.IsVisible = tag == "home";
        InstancesPage.IsVisible = tag == "instances";
        DownloadsPage.IsVisible = tag == "downloads";
        OthersPage.IsVisible = tag == "others";
        SettingsPage.IsVisible = tag == "settings";
        AboutPage.IsVisible = tag == "about";
        if (tag is "home" or "instances")
        {
            RefreshHome();
        }

        if (tag == "others")
        {
            RefreshOthers();
        }
    }

    private void RefreshOthers()
    {
        if (OthersInstanceText is null)
        {
            return;
        }

        OthersInstanceText.Text = InstanceList?.SelectedItem is InstanceItem item
            ? $"{item.Name} · {item.Version}" + (string.IsNullOrWhiteSpace(item.Group) ? string.Empty : $" · {item.Group}")
            : "未选择实例（先到实例页选一个）";
    }

    private void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            ApplyNav(tag);
        }
    }

    private void OnGoInstancesClick(object? sender, RoutedEventArgs e) => ApplyNav("instances");

    private void OnOthersAccountsClick(object? sender, RoutedEventArgs e)
    {
        new AccountsWindow(_layout, InstanceList.SelectedItem is InstanceItem item ? item.Config : null)
            .ShowDialog(this);
    }

    private void OnOthersDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is InstanceItem item)
        {
            new DiagnosticsWindow(item.Config.Name, _layout).ShowDialog(this);
        }
    }

    private void OnOthersLogClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is InstanceItem item)
        {
            new LogWindow(item.Config.Name, _layout).Show(this);
        }
    }

    private void OnOthersOpenInstanceClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is InstanceItem item)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", _layout.InstanceDir(item.Config.Name)) { UseShellExecute = true });
        }
    }

    private void OnOthersOpenDataClick(object? sender, RoutedEventArgs e) => OpenDirectory(_layout.Root);

    private void OnOthersOpenLogsClick(object? sender, RoutedEventArgs e) => OpenDirectory(Path.Combine(_layout.Root, "logs"));

    private void OnOthersOpenCacheClick(object? sender, RoutedEventArgs e) => OpenDirectory(Path.Combine(_layout.Root, "cache"));

    private void OnOthersOpenTrashClick(object? sender, RoutedEventArgs e) => OpenDirectory(Path.Combine(_layout.Root, "trash"));

    private DateTime _trashClearRequestedAt = DateTime.MinValue;

    private void OnOthersClearTrashClick(object? sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _trashClearRequestedAt).TotalSeconds > 5)
        {
            _trashClearRequestedAt = DateTime.Now;
            OthersStatus.Text = "再点一次「清空回收站」确认（5 秒内有效）";
            return;
        }

        _trashClearRequestedAt = DateTime.MinValue;
        try
        {
            var trash = Path.Combine(_layout.Root, "trash");
            if (Directory.Exists(trash))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(trash))
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }

            OthersStatus.Text = "回收站已清空";
        }
        catch (Exception exception)
        {
            OthersStatus.Text = $"清空失败：{exception.Message}";
        }
    }

    private void OnOthersOpenGuideClick(object? sender, RoutedEventArgs e)
    {
        var guide = Path.Combine(AppContext.BaseDirectory, "docs", "DEVELOPER-GUIDE.md");
        if (!File.Exists(guide))
        {
            guide = Path.Combine(_layout.Root, "docs", "DEVELOPER-GUIDE.md");
        }

        if (File.Exists(guide))
        {
            Process.Start(new ProcessStartInfo(guide) { UseShellExecute = true });
        }
        else
        {
            OthersStatus.Text = "仓库里的 docs/DEVELOPER-GUIDE.md 在发布目录不存在";
        }
    }

    private void OnOthersAboutClick(object? sender, RoutedEventArgs e) => ApplyNav("about");

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private static readonly (string Start, string End)[] HomeGradients =
    [
        ("#1F3B57", "#0B1521"),
        ("#3A2440", "#120B16"),
        ("#1E3B2F", "#0A1410"),
        ("#3E2A1E", "#150F0B"),
        ("#24304F", "#0C1019"),
        ("#40202B", "#150A0F"),
        ("#203A40", "#0A1214"),
        ("#332E1F", "#120F0A"),
    ];

    private void RefreshHome()
    {
        if (HomeName is null)
        {
            return;
        }

        HomeInstanceList.ItemsSource = _allInstances;

        if (InstanceList?.SelectedItem is not InstanceItem item)
        {
            HomeName.Text = "没有实例";
            HomeWatermark.Text = string.Empty;
            HomeVersionLabel.Text = "先去「下载」页创建一个实例";
            HomeLaunchButton.IsEnabled = false;
            HomeBackground.Background = new SolidColorBrush(Color.Parse("#141419"));
            return;
        }

        HomeName.Text = item.Name;
        HomeWatermark.Text = item.Initial;
        HomeVersionLabel.Text = $"{item.Version} · {item.Config.MemoryMb} MB"
                                + (string.IsNullOrWhiteSpace(item.Group) ? string.Empty : $" · {item.Group}");
        HomeLaunchButton.IsEnabled = true;
        HomeInstanceList.SelectedItem = item;

        var gradient = HomeGradients[Math.Abs(StringComparer.Ordinal.GetHashCode(item.Name)) % HomeGradients.Length];
        HomeBackground.Background = new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.Parse(gradient.Start), 0),
                new GradientStop(Color.Parse(gradient.End), 1),
            },
        };

        RefreshAccount(item);
    }

    private void OnInstancePickerClick(object? sender, RoutedEventArgs e)
    {
        InstancePopup.IsOpen = !InstancePopup.IsOpen;
    }

    private void OnHomeInstanceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (HomeInstanceList.SelectedItem is not InstanceItem item)
        {
            return;
        }

        if (ReferenceEquals(item, InstanceList.SelectedItem))
        {
            return;
        }

        InstanceList.SelectedItem = item;
        InstancePopup.IsOpen = false;
    }

    private void RefreshAccount(InstanceItem? item)
    {
        if (AccountName is null)
        {
            return;
        }

        var key = item?.Config.AccountKey;
        string? name = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            try
            {
                name = _accounts.LoadAsync(key!, CancellationToken.None).GetAwaiter().GetResult()?.Profile.Name;
            }
            catch (Exception)
            {
                name = null;
            }
        }

        name ??= item?.Config.Username;
        name ??= "Player";
        AccountName.Text = (item?.Config.AccountKey is null ? "离线 · " : string.Empty) + name;
        AccountInitial.Text = string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();
    }

    private async void OnAccountsClick(object? sender, RoutedEventArgs e)
    {
        await new AccountsWindow(
                _layout,
                InstanceList.SelectedItem is InstanceItem item ? item.Config : null)
            .ShowDialog(this);
        if (InstanceList.SelectedItem is InstanceItem current)
        {
            RefreshAccount(current);
        }
    }

    private InstanceItem[] _allInstances = [];

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

    private void ApplyLanguage()
    {
        Title = Localization.T("title");
        RefreshButton.Content = Localization.T("toolbar.refresh");
        LaunchButton.Content = Localization.T("toolbar.launch");
        OpenFolderButton.Content = Localization.T("toolbar.open");
        DiagnosticsButton.Content = Localization.T("toolbar.diagnostics");
        LogButton.Content = Localization.T("toolbar.log");
        SearchBox.PlaceholderText = Localization.T("search.placeholder");

        NavHomeButton.Content = Localization.T("nav.home");
        NavInstancesButton.Content = Localization.T("nav.instances");
        NavDownloadsButton.Content = Localization.T("nav.downloads");
        NavOthersButton.Content = Localization.T("nav.others");
        NavSettingsButton.Content = Localization.T("nav.settings");
        NavAboutButton.Content = Localization.T("nav.about");
    }

    private void RefreshInstances()
    {
        _allInstances = _store.List().Select(config => new InstanceItem(config)).ToArray();

        if (GroupBox is not null)
        {
            var groups = _allInstances
                .Select(item => item.Group)
                .Where(group => !string.IsNullOrWhiteSpace(group))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            GroupBox.ItemsSource = new[] { "全部分组" }.Concat(groups).ToArray();
            GroupBox.SelectedIndex = 0;
        }

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
        var group = GroupBox?.SelectedItem as string;
        var items = _allInstances
            .Where(item => string.IsNullOrWhiteSpace(query)
                || item.Name.Contains(query!, StringComparison.OrdinalIgnoreCase)
                || item.Version.Contains(query!, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(group)
                || group == "全部分组"
                || string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase))
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

    private void OnGroupChanged(object? sender, SelectionChangedEventArgs e) => ApplyInstanceFilter();

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
        RefreshHome();
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

    private async void RefreshContent()
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
        MoveUpButton.IsVisible = kind == ContentKind.ResourcePacks;
        MoveDownButton.IsVisible = kind == ContentKind.ResourcePacks;
        if (kind == ContentKind.Datapacks && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            ContentList.ItemsSource = null;
            ContentStatus.Text = "这个实例还没有世界；先进入游戏创建一个，或添加世界目录";
            return;
        }

        var service = new InstanceContentService(_layout, _store);
        var files = service.List(item.Config.Name, kind, SelectedWorld);
        if (kind == ContentKind.ResourcePacks)
        {
            var order = new ResourcePackOrder(_layout, _store).List(item.Config.Name);
            var rank = order
                .Select((id, index) => (Name: Path.GetFileName(id), Index: index))
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group2 => group2.Key, group2 => group2.First().Index, StringComparer.OrdinalIgnoreCase);
            files = files
                .OrderBy(file => rank.TryGetValue(file.Name, out var index) ? index : int.MaxValue)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var directory = service.DirectoryFor(item.Config.Name, kind, SelectedWorld);
        var version = ++_contentVersion;
        var items = await Task.Run(() => files
            .Select(file => new ContentItem(
                file,
                kind == ContentKind.Mods ? DescribeMod(Path.Combine(directory, file.Name)) : null))
            .ToArray());

        // 期间用户切了实例/类型就丢弃这次结果
        if (version != _contentVersion || InstanceList.SelectedItem is not InstanceItem current || current.Config.Name != item.Config.Name)
        {
            return;
        }

        ContentList.ItemsSource = items;
        ContentTitle.Text = $"内容 · {item.Config.Name}";
        ContentStatus.Text = files.Count == 0
            ? $"空（{InstanceContentService.FolderName(kind)}）"
            : $"{files.Count} 个 · 目录 {directory}";
    }

    private int _contentVersion;

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

    private void OnLogClick(object? sender, RoutedEventArgs e)
    {
        if (InstanceList.SelectedItem is not InstanceItem item)
        {
            StatusText.Text = "先选一个实例";
            return;
        }

        new LogWindow(item.Config.Name, _layout).Show(this);
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e) => MoveSelectedPack(-1);

    private void OnMoveDownClick(object? sender, RoutedEventArgs e) => MoveSelectedPack(1);

    private void MoveSelectedPack(int delta)
    {
        if (InstanceList.SelectedItem is not InstanceItem item || ContentList.SelectedItem is not ContentItem content)
        {
            ContentStatus.Text = "先选中一个资源包";
            return;
        }

        var order = new ResourcePackOrder(_layout, _store);
        var packId = order.List(item.Config.Name).FirstOrDefault(id =>
            string.Equals(Path.GetFileName(id), content.File.Name, StringComparison.OrdinalIgnoreCase));
        if (packId is null)
        {
            ContentStatus.Text = "这个资源包不在 options.txt 的顺序里";
            return;
        }

        ContentStatus.Text = order.Move(item.Config.Name, packId, delta) ? "顺序已调整（已备份 options.txt.bak）" : "已经到顶/到底";
        RefreshContent();
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
            var config = item.Config;
            if (string.IsNullOrWhiteSpace(config.AccountKey)
                && string.IsNullOrWhiteSpace(config.Username)
                && !string.IsNullOrWhiteSpace(AppServices.Settings.DefaultAccountKey))
            {
                var session = await _accounts.LoadAsync(AppServices.Settings.DefaultAccountKey!, CancellationToken.None);
                if (session is not null)
                {
                    config = config with { Username = session.Profile.Name };
                }
            }

            var outcome = await _launcher.LaunchAsync(
                config,
                message => Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText.Text = message),
                CancellationToken.None,
                AppServices.Settings.AutoInstallJava);
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

}


/// <summary>实例列表的展示项：只暴露界面需要的字段。</summary>
public sealed class InstanceItem(InstanceConfig config)
{
    public InstanceConfig Config { get; } = config;

    public string Name => Config.Name;

    public string Version => Config.VersionId;

    public string? Group => Config.Group;

    public string Initial => string.IsNullOrEmpty(Config.Name) ? "?" : Config.Name[..1].ToUpperInvariant();

    public string Detail
        => $"{Config.MemoryMb} MB · {Config.Username ?? "Player"} · {Config.CreatedAt:yyyy-MM-dd}"
           + (string.IsNullOrWhiteSpace(Config.Group) ? string.Empty : $" · {Config.Group}");
}

/// <summary>鸣谢名单的一行（来源 Assets/credits.json）。</summary>
public sealed record CreditItem(string Name, string Role);

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
