using Avalonia.Controls;
using Avalonia.Interactivity;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;
using EsLuncher.Core.Loaders;

namespace EsLuncher.App;

public partial class ModrinthDetailWindow : Window
{
    private readonly ModrinthHit _hit;
    private readonly ContentKind _kind;
    private readonly bool _modpack;
    private readonly string? _instanceName;
    private readonly string _minecraftVersion;
    private readonly string? _loader;
    private readonly string? _world;
    private readonly ModrinthClient _client = new(new HttpRemoteFileClient());
    private ModrinthProject? _project;

    /// <summary>设计器/XAML 加载用。</summary>
    public ModrinthDetailWindow()
        : this(new ModrinthHit("", "", "(未选择)", "", 0), ContentKind.Mods, false, null, "1.18.2", null, null)
    {
    }

    public ModrinthDetailWindow(
        ModrinthHit hit,
        ContentKind kind,
        bool modpack,
        string? instanceName,
        string minecraftVersion,
        string? loader,
        string? world)
    {
        InitializeComponent();
        _hit = hit;
        _kind = kind;
        _modpack = modpack;
        _instanceName = instanceName;
        _minecraftVersion = minecraftVersion;
        _loader = loader;
        _world = world;

        Title = $"详情 · {hit.Title}";
        TitleText.Text = hit.Title;
        MetaText.Text = $"{hit.Slug} · MC {minecraftVersion}";
        DownloadsText.Text = $"{hit.Downloads:N0} 下载";
        DescriptionBox.Text = hit.Description;
        InstallButton.IsVisible = true;
        if (modpack)
        {
            InstanceNameBox.IsVisible = true;
            InstallButton.Content = "安装为实例";
            StatusText.Text = Localization.T("detail.modpackHint");
        }

        Loaded += async (_, _) =>
        {
            var icon = await IconCache.GetAsync(hit.IconUrl);
            if (icon is not null)
            {
                IconImage.Source = icon;
            }

            await LoadAsync();
        };
    }

    public event Action? InstanceCreated;

    private async Task LoadAsync()
    {
        try
        {
            _project = await _client.GetProjectAsync(_hit.Slug, CancellationToken.None);
            if (_project is not null)
            {
                TitleText.Text = _project.Title;
                CategoriesText.Text = _project.Categories.Count == 0
                    ? string.Empty
                    : "分类：" + string.Join(" · ", _project.Categories);
                DownloadsText.Text = $"{_project.Downloads:N0} 下载";
                DescriptionBox.Text = string.IsNullOrWhiteSpace(_project.Body)
                    ? _project.Description
                    : StripMarkdown(_project.Body);
            }

            var versions = await _client.GetVersionsAsync(_hit.Slug, _minecraftVersion, _loader, CancellationToken.None);
            VersionsList.ItemsSource = versions
                .Select(version => new VersionInfoItem(
                    $"{version.VersionNumber}   {string.Join(", ", version.GameVersions ?? [])}",
                    version))
                .ToArray();
            VersionsList.SelectedIndex = versions.Count > 0 ? 0 : -1;
            StatusText.Text = Localization.F("detail.versionsInfo", versions.Count, _minecraftVersion);
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("detail.loadFailed", exception.Message);
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not VersionInfoItem selected)
        {
            StatusText.Text = Localization.T("detail.pickVersion");
            return;
        }

        if (_modpack)
        {
            await InstallModpackAsync(selected.Version);
            return;
        }

        if (string.IsNullOrWhiteSpace(_instanceName))
        {
            StatusText.Text = Localization.T("detail.noTarget");
            return;
        }

        try
        {
            StatusText.Text = Localization.F("detail.installing", selected.Version.VersionNumber);
            var content = new InstanceContentService(AppServices.Layout, new InstanceStore(AppServices.Layout));
            var file = await _client.InstallVersionAsync(
                content,
                _instanceName!,
                selected.Version,
                _kind,
                _world,
                CancellationToken.None);
            var required = selected.Version.Dependencies.Count(dependency => dependency.DependencyType == "required");
            StatusText.Text = required > 0
                ? Localization.F("detail.installedDeps", file, required)
                : Localization.F("detail.installed", file);
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("detail.installFailed", exception.Message);
        }
    }

    private async Task InstallModpackAsync(ModrinthVersion version)
    {
        var name = InstanceNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = _hit.Slug;
        }

        var layout = AppServices.Layout;
        var store = new InstanceStore(layout);
        if (store.TryLoad(name!) is not null)
        {
            StatusText.Text = Localization.F("panel.exists", name);
            return;
        }

        var file = version.Files.FirstOrDefault(candidate =>
            candidate.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("这个版本没有 .mrpack 文件");

        try
        {
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
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("detail.installFailed", exception.Message);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private static string StripMarkdown(string markdown)
    {
        // 简单去标签：详情页只要可读文本，不追求完整 Markdown 渲染
        var text = markdown.Replace("\r\n", "\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"!\[[^\]]*\]\([^)]*\)", string.Empty);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]*)\]\([^)]*\)", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[#*`>_~]", string.Empty);
        return text.Length > 4000 ? text[..4000] + "\n…（截断）" : text;
    }
}

public sealed record VersionInfoItem(string Label, ModrinthVersion Version);
