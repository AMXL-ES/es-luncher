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
        InstallButton.IsVisible = !modpack;
        if (modpack)
        {
            StatusText.Text = "整合包请在列表页填写实例名后安装（详情页仅浏览）";
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
            StatusText.Text = $"可用版本：{versions.Count} 个 · 目标 MC {_minecraftVersion}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"加载详情失败：{exception.Message}";
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_instanceName))
        {
            StatusText.Text = "没有目标实例：请从下载页进入";
            return;
        }

        if (VersionsList.SelectedItem is not VersionInfoItem selected)
        {
            StatusText.Text = "先选中一个版本";
            return;
        }

        try
        {
            StatusText.Text = $"安装 {selected.Version.VersionNumber} …";
            var content = new InstanceContentService(AppServices.Layout, new InstanceStore(AppServices.Layout));
            var file = await _client.InstallVersionAsync(
                content,
                _instanceName!,
                selected.Version,
                _kind,
                _world,
                CancellationToken.None);
            var required = selected.Version.Dependencies.Count(dependency => dependency.DependencyType == "required");
            StatusText.Text = $"已安装 → {file}" +
                              (required > 0 ? $"（该版本有 {required} 个必需前置，安装后可在诊断里检查）" : string.Empty);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"安装失败：{exception.Message}";
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
