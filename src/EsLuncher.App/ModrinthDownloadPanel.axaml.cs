using Avalonia.Controls;
using Avalonia.Interactivity;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Loaders;

namespace EsLuncher.App;

/// <summary>下载页里的 Modrinth 浏览/搜索面板：打开即出列表，可切分类与排序；支持详情与安装。</summary>
public partial class ModrinthDownloadPanel : UserControl
{
    private static readonly Dictionary<ContentKind, (string Label, string Slugs)[]> Categories = new()
    {
        [ContentKind.Mods] =
        [
            ("全部", ""), ("优化", "optimization"), ("科技", "technology"), ("魔法", "magic"),
            ("冒险", "adventure"), ("装饰", "decoration"), ("工具", "utility"), ("库", "library"),
            ("世界生成", "worldgen"), ("存储", "storage"), ("食物", "food"),
        ],
        [ContentKind.Datapacks] =
        [
            ("全部", ""), ("冒险", "adventure"), ("装饰", "decoration"), ("经济", "economy"),
            ("装备", "equipment"), ("游戏机制", "game-mechanics"), ("魔法", "magic"), ("生物", "mobs"),
            ("优化", "optimization"), ("科技", "technology"), ("工具", "utility"), ("世界生成", "worldgen"),
        ],
        [ContentKind.ResourcePacks] = [("全部", "")],
        [ContentKind.ShaderPacks] = [("全部", "")],
    };

    private static readonly (string Label, string? Index)[] Sorts =
    [
        ("下载量", "downloads"), ("相关度", null), ("关注", "follows"), ("最新", "newest"), ("最近更新", "updated"),
    ];

    private readonly ModrinthClient _client = new(new HttpRemoteFileClient());
    private string? _instanceName;
    private string _minecraftVersion = "1.18.2";
    private string? _loader;
    private string? _world;

    public ModrinthDownloadPanel()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            NameBox.IsVisible = ModpackMode;
            InstallButton.Content = ModpackMode ? "安装整合包" : "安装选中";
            FillFilters();
            await SearchAsync(QueryBox.Text ?? string.Empty);
        };
    }

    public ContentKind Kind { get; set; } = ContentKind.Mods;

    public bool ModpackMode { get; set; }

    public event Action? InstanceCreated;

    public void SetContext(string? instanceName, string minecraftVersion, string? loader, string? world)
    {
        _instanceName = instanceName;
        _minecraftVersion = minecraftVersion;
        _loader = loader;
        _world = world;
        StatusText.Text = string.IsNullOrWhiteSpace(instanceName)
            ? Localization.T("panel.pickInstance")
            : Localization.F("panel.target", instanceName, minecraftVersion + (loader is null ? string.Empty : " / " + loader));
    }

    private void FillFilters()
    {
        if (CategoryBox.ItemsSource is not null)
        {
            return;
        }

        var categories = Categories.TryGetValue(Kind, out var list) ? list : [("全部", "")];
        CategoryBox.ItemsSource = categories.Select(category => category.Label).ToArray();
        CategoryBox.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
    }

    private async void OnFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await SearchAsync(QueryBox.Text ?? string.Empty);
        }
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
        => await SearchAsync(QueryBox.Text?.Trim() ?? string.Empty);

    public async Task SearchAsync(string query)
    {
        try
        {
            var category = string.Empty;
            if (CategoryBox.SelectedItem is string label
                && Categories.TryGetValue(Kind, out var list))
            {
                category = list.FirstOrDefault(entry => entry.Label == label).Slugs ?? string.Empty;
            }

            var sortIndex = SortBox.SelectedIndex >= 0 && SortBox.SelectedIndex < Sorts.Length
                ? Sorts[SortBox.SelectedIndex].Index
                : "downloads";
            var categoriesFacet = string.IsNullOrWhiteSpace(category) ? null : new[] { category };

            StatusText.Text = Localization.T("panel.loading");
            var hits = await _client.SearchAsync(
                query,
                _minecraftVersion,
                _loader,
                30,
                CancellationToken.None,
                Kind,
                ModpackMode ? "modpack" : null,
                sortIndex,
                categoriesFacet);
            var items = await Task.WhenAll(hits.Select(async hit =>
                new ModrinthItem(
                    hit,
                    $"{hit.Downloads:N0} 下载 · {hit.Description}",
                    await IconCache.GetAsync(hit.IconUrl))));
            HitsList.ItemsSource = items;
            StatusText.Text = query.Length == 0
                ? Localization.F("panel.browse", hits.Count, Sorts[Math.Max(SortBox.SelectedIndex, 0)].Label)
                : Localization.F("panel.found", hits.Count);

            if (Environment.GetEnvironmentVariable("ESLUNCHER_UI_OPEN_DETAIL") == "1" && items.Length > 0)
            {
                HitsList.SelectedIndex = 0;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDetailClick(this, new RoutedEventArgs()));
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("panel.loadFailed", exception.Message);
        }
    }

    private void OnDetailClick(object? sender, RoutedEventArgs e)
    {
        if (HitsList.SelectedItem is not ModrinthItem item)
        {
            StatusText.Text = Localization.T("panel.pickResult");
            return;
        }

        var window = new ModrinthDetailWindow(item.Hit, Kind, ModpackMode, _instanceName, _minecraftVersion, _loader, _world);
        window.InstanceCreated += () => InstanceCreated?.Invoke();
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (HitsList.SelectedItem is not ModrinthItem item)
        {
            StatusText.Text = Localization.T("panel.pickResult");
            return;
        }

        ProgressBar.IsVisible = true;
        try
        {
            if (ModpackMode)
            {
                await InstallModpackAsync(item.Hit);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_instanceName))
                {
                    StatusText.Text = Localization.T("panel.pickInstance");
                    return;
                }

                var content = new InstanceContentService(AppServices.Layout, new InstanceStore(AppServices.Layout));
                var file = await _client.InstallAsync(
                    content,
                    _instanceName!,
                    item.Hit.Slug,
                    _minecraftVersion,
                    _loader,
                    CancellationToken.None,
                    Kind,
                    _world);
                StatusText.Text = Localization.F("panel.installed", item.Hit.Title, file);
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("panel.installFailed", exception.Message);
        }
        finally
        {
            ProgressBar.IsVisible = false;
        }
    }

    private async Task InstallModpackAsync(ModrinthHit hit)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = hit.Slug;
        }

        var layout = AppServices.Layout;
        var store = new InstanceStore(layout);
        if (store.TryLoad(name!) is not null)
        {
            StatusText.Text = Localization.F("panel.exists", name);
            return;
        }

        StatusText.Text = Localization.F("panel.readingPack", hit.Title);
        var versions = await _client.GetVersionsAsync(hit.Slug, _minecraftVersion, null, CancellationToken.None);
        var version = versions.FirstOrDefault()
            ?? throw new InvalidOperationException("没有可用版本");
        var file = version.Files.FirstOrDefault(candidate =>
            candidate.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("这个项目没有 .mrpack 文件");

        using var client = new HttpRemoteFileClient();
        var downloader = new FileDownloader(client);
        var zipPath = Path.Combine(layout.Root, "cache", "packs", file.Filename);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        StatusText.Text = Localization.F("panel.downloading", file.Filename, file.Size / 1048576.0);
        var downloaded = await downloader.DownloadAsync(
            new DownloadJob { Primary = new Uri(file.Url), TargetPath = zipPath },
            CancellationToken.None);
        if (downloaded.Status == FileDownloadStatus.Failed)
        {
            StatusText.Text = Localization.F("panel.downloadFailed", downloaded.Error);
            return;
        }

        var installer = new ModpackInstaller(
            layout,
            store,
            new InstancePackService(layout, store, new InstanceContentService(layout, store)),
            new LoaderProvisioner(layout, downloader, client),
            new VersionFetcher(layout, downloader),
            client);
        var instance = await installer.InstallAsync(zipPath, name!, 2048, CancellationToken.None);
        StatusText.Text = Localization.F("panel.created", instance.Name, instance.VersionId);
        InstanceCreated?.Invoke();
    }
}
