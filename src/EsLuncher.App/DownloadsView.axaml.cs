using Avalonia.Controls;
using Avalonia.Interactivity;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Java;
using EsLuncher.Core.Launching;
using EsLuncher.Core.Loaders;
using EsLuncher.Core.Metadata;

namespace EsLuncher.App;

public partial class DownloadsView : UserControl
{
    private VersionManifest? _manifest;
    private CancellationTokenSource? _cancellation;
    private InstanceConfig[] _instances = [];

    public DownloadsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadInstances();
            await LoadManifestAsync(refresh: false);
        };
    }

    private void LoadInstances()
    {
        _instances = new InstanceStore(AppServices.Layout).List().ToArray();
        InstanceCombo.ItemsSource = _instances.Select(instance => instance.Name).ToArray();
        if (_instances.Length > 0 && InstanceCombo.SelectedIndex < 0)
        {
            InstanceCombo.SelectedIndex = 0;
        }

        UpdateContext();
    }

    private InstanceConfig? SelectedInstance
    {
        get
        {
            var index = InstanceCombo.SelectedIndex;
            return index >= 0 && index < _instances.Length ? _instances[index] : null;
        }
    }

    private void OnInstanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateContext();
        LoadWorlds();
    }

    private void OnWorldChanged(object? sender, SelectionChangedEventArgs e) => UpdateContext();

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e) => UpdateContext();

    private void LoadWorlds()
    {
        if (WorldCombo is null || SelectedInstance is null)
        {
            return;
        }

        var service = new InstanceContentService(AppServices.Layout, new InstanceStore(AppServices.Layout));
        var worlds = service.List(SelectedInstance.Name, ContentKind.Worlds).Select(world => world.Name).ToArray();
        WorldCombo.ItemsSource = worlds;
        if (worlds.Length > 0)
        {
            WorldCombo.SelectedIndex = 0;
        }

        UpdateContext();
    }

    private void UpdateContext()
    {
        if (ModsPanel is null)
        {
            return;
        }

        var instance = SelectedInstance;
        var (minecraftVersion, loader) = instance is null
            ? ("1.18.2", (string?)null)
            : InstanceVersionInfo.Derive(instance.VersionId);
        var modrinthLoader = loader switch
        {
            "fabric-loader" => "fabric",
            "quilt-loader" => "quilt",
            var other => other,
        };
        var world = WorldCombo?.SelectedItem as string;

        ModpackPanel.SetContext(instance?.Name, minecraftVersion, modrinthLoader, null);
        ModsPanel.SetContext(instance?.Name, minecraftVersion, modrinthLoader, null);
        ResourcePacksPanel.SetContext(instance?.Name, minecraftVersion, modrinthLoader, null);
        ShadersPanel.SetContext(instance?.Name, minecraftVersion, modrinthLoader, null);
        DatapacksPanel.SetContext(instance?.Name, minecraftVersion, modrinthLoader, world);

        ContextText.Text = instance is null
            ? "还没有实例：先用「游戏版本」页下载并创建"
            : $"当前：{instance.Name} · MC {minecraftVersion}{(loader is null ? string.Empty : " / " + loader)}" +
              (string.IsNullOrWhiteSpace(world) ? string.Empty : $" · 世界 {world}");
    }

    private async Task LoadManifestAsync(bool refresh)
    {
        try
        {
            StatusText.Text = refresh ? "刷新版本清单 …" : "读取版本清单（已缓存则不出网）…";
            using var client = new HttpRemoteFileClient();
            var catalog = new VersionCatalog(AppServices.Layout, new FileDownloader(client));
            _manifest = await catalog.GetManifestAsync(refresh, CancellationToken.None);
            ApplyFilter();
            StatusText.Text = $"清单：{_manifest.Versions.Count} 个版本（latest {_manifest.Latest.Release}）";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"读取清单失败：{exception.Message}";
        }
    }

    private void ApplyFilter()
    {
        if (_manifest is null)
        {
            return;
        }

        var type = FilterBox.SelectedIndex switch
        {
            0 => "release",
            1 => "snapshot",
            _ => VersionQuery.AllTypes,
        };
        var items = VersionQuery.Latest(_manifest, type, 80)
            .Select(entry => new VersionItem(entry.Id, entry.Type))
            .ToArray();
        VersionList.ItemsSource = items;
        if (items.Length > 0)
        {
            VersionList.SelectedIndex = 0;
        }
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private async void OnRefreshClick(object? sender, RoutedEventArgs e) => await LoadManifestAsync(refresh: true);

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionItem item)
        {
            StatusText.Text = "先选一个版本";
            return;
        }

        _cancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        try
        {
            var report = await FetchVersionAsync(item.Id, _cancellation.Token);
            StatusText.Text = report is null
                ? StatusText.Text
                : report.Success
                    ? $"完成：{report.VersionId} 计划 {report.FilesPlanned}，下载 {report.Downloaded}，跳过 {report.Skipped}，失败 {report.Failed}"
                    : $"有 {report.Failed} 个文件失败（{report.VersionId}）";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消下载";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"下载失败：{exception.Message}";
        }
        finally
        {
            EndDownloadUi();
        }
    }

    private async void OnDownloadAndCreateClick(object? sender, RoutedEventArgs e)
    {
        if (VersionList.SelectedItem is not VersionItem item)
        {
            StatusText.Text = "先选一个版本";
            return;
        }

        _cancellation = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        var layout = AppServices.Layout;
        try
        {
            var report = await FetchVersionAsync(item.Id, _cancellation.Token);
            if (report is null || !report.Success)
            {
                StatusText.Text = $"版本下载未完成：失败 {report?.Failed ?? 1} 个文件";
                return;
            }

            var versionId = item.Id;
            var loader = LoaderKindOf(LoaderBox.SelectedIndex);
            if (loader is { } kind)
            {
                StatusText.Text = $"安装 {kind} 加载器 …";
                using var client = new HttpRemoteFileClient();
                var downloader = new FileDownloader(client);
                var provisioner = new LoaderProvisioner(layout, downloader, client);
                var java = kind is LoaderKind.Forge or LoaderKind.NeoForge
                    ? await SelectJavaForVersionAsync(layout, downloader, item.Id, _cancellation.Token)
                    : null;
                versionId = await provisioner
                    .InstallAsync(kind, item.Id, null, java, _cancellation.Token);

                StatusText.Text = $"下载 {kind} 依赖库 …";
                var fetcher = new VersionFetcher(layout, downloader);
                var loaderReport = await fetcher.FetchAsync(
                    versionId,
                    new FetchOptions { Selection = new FetchSelection(Client: false, Libraries: true, Assets: false, Natives: true) },
                    MakeProgress(),
                    _cancellation.Token);
                if (!loaderReport.Success)
                {
                    StatusText.Text = $"加载器依赖下载失败：{loaderReport.Failed} 个文件";
                    return;
                }
            }

            var name = string.IsNullOrWhiteSpace(InstanceNameBox.Text)
                ? versionId
                : InstanceNameBox.Text!.Trim();
            var instance = new InstanceStore(layout).Create(
                name,
                versionId,
                AppServices.Settings.DefaultMemoryMb,
                username: null);
            StatusText.Text = $"已创建实例 {instance.Name} ← {instance.VersionId}（可在「实例」页启动）";
            LoadInstances();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"创建实例失败：{exception.Message}";
        }
        finally
        {
            EndDownloadUi();
        }
    }

    private async Task<FetchReport?> FetchVersionAsync(string versionId, CancellationToken cancellationToken)
    {
        ProgressBar.IsVisible = true;
        ProgressBar.Value = 0;
        using var client = new HttpRemoteFileClient();
        var fetcher = new VersionFetcher(AppServices.Layout, new FileDownloader(client));
        return await fetcher.FetchAsync(
            versionId,
            new FetchOptions { Concurrency = 8 },
            MakeProgress(),
            cancellationToken);
    }

    private Progress<DownloadProgress> MakeProgress() => new(value =>
    {
        var percent = value.FilesTotal == 0 ? 0 : value.FilesDone * 100.0 / value.FilesTotal;
        ProgressBar.Value = percent;
        StatusText.Text = $"{value.FilesDone}/{value.FilesTotal} 文件 · {value.BytesDownloaded / 1048576.0:F1} MiB · {value.CurrentPath}";
    });

    private void EndDownloadUi()
    {
        CancelButton.IsEnabled = false;
        ProgressBar.IsVisible = false;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private static LoaderKind? LoaderKindOf(int selectedIndex) => selectedIndex switch
    {
        1 => LoaderKind.Fabric,
        2 => LoaderKind.Quilt,
        3 => LoaderKind.Forge,
        _ => null,
    };

    private static async Task<JavaRuntime?> SelectJavaForVersionAsync(
        DataLayout layout,
        FileDownloader downloader,
        string mcVersion,
        CancellationToken cancellationToken)
    {
        var catalog = new VersionCatalog(layout, downloader);
        var manifest = await catalog.GetManifestAsync(false, cancellationToken);
        var version = await catalog.GetVersionAsync(mcVersion, manifest, false, cancellationToken);
        var required = version.JavaVersion?.MajorVersion ?? 8;
        return JavaSelector.Select(required, JavaDiscovery.Discover([layout.JavaDir]), null).Runtime;
    }
}

public sealed record VersionItem(string Id, string Type)
{
    public string Badge => Type == "release" ? "正式版" : "快照";
}
