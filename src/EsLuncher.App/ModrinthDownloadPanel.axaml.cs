using Avalonia.Controls;
using Avalonia.Interactivity;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Loaders;

namespace EsLuncher.App;

/// <summary>下载页里的 Modrinth 搜索面板：按类型装进选中实例；ModpackMode 时把 .mrpack 装成新实例。</summary>
public partial class ModrinthDownloadPanel : UserControl
{
    private readonly ModrinthClient _client = new(new HttpRemoteFileClient());
    private string? _instanceName;
    private string _minecraftVersion = "1.18.2";
    private string? _loader;
    private string? _world;

    public ModrinthDownloadPanel()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            NameBox.IsVisible = ModpackMode;
            InstallButton.Content = ModpackMode ? "安装整合包" : "安装选中";
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
            ? "先在顶部选一个实例"
            : $"目标实例：{instanceName} · MC {minecraftVersion}{(loader is null ? string.Empty : " / " + loader)}";
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        try
        {
            StatusText.Text = "搜索中 …";
            var hits = await _client.SearchAsync(
                query,
                _minecraftVersion,
                _loader,
                20,
                CancellationToken.None,
                Kind,
                ModpackMode ? "modpack" : null);
            HitsList.ItemsSource = hits
                .Select(hit => new ModrinthItem(hit, $"{hit.Downloads:N0} 下载 · {hit.Description}"))
                .ToArray();
            StatusText.Text = $"找到 {hits.Count} 条";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"搜索失败：{exception.Message}";
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (HitsList.SelectedItem is not ModrinthItem item)
        {
            StatusText.Text = "先选中一个结果";
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
                    StatusText.Text = "先在顶部选一个实例";
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
                StatusText.Text = $"已安装 {item.Hit.Title} → {file}";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"安装失败：{exception.Message}";
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
            StatusText.Text = $"实例已存在：{name}";
            return;
        }

        StatusText.Text = $"读取整合包 {hit.Title} …";
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
        StatusText.Text = $"下载 {file.Filename}（{file.Size / 1048576.0:F1} MiB）…";
        var downloaded = await downloader.DownloadAsync(
            new DownloadJob { Primary = new Uri(file.Url), TargetPath = zipPath },
            CancellationToken.None);
        if (downloaded.Status == FileDownloadStatus.Failed)
        {
            StatusText.Text = $"下载失败：{downloaded.Error}";
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
        StatusText.Text = $"已创建实例 {instance.Name} ← {instance.VersionId}";
        InstanceCreated?.Invoke();
    }
}
