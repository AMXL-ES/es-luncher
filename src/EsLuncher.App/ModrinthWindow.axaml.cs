using Avalonia.Controls;
using Avalonia.Interactivity;
using EsLuncher.Core.Content;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class ModrinthWindow : Window
{
    private readonly string _instance;
    private readonly DataLayout _layout;
    private readonly string _minecraftVersion;
    private readonly string? _loader;
    private readonly ContentKind _kind;
    private readonly string? _world;
    private readonly ModrinthClient _client = new(new HttpRemoteFileClient());

    /// <summary>设计器/XAML 加载用的无参构造。</summary>
    public ModrinthWindow()
        : this("(未选择实例)", AppServices.Layout, "1.18.2", null)
    {
    }

    public ModrinthWindow(
        string instanceName,
        DataLayout layout,
        string minecraftVersion,
        string? loader,
        ContentKind kind = ContentKind.Mods,
        string? world = null)
    {
        InitializeComponent();
        _instance = instanceName;
        _layout = layout;
        _minecraftVersion = minecraftVersion;
        _loader = loader;
        _kind = kind;
        _world = world;

        var kindLabel = kind switch
        {
            ContentKind.ResourcePacks => "资源包",
            ContentKind.ShaderPacks => "光影",
            ContentKind.Datapacks => $"数据包 · {world}",
            _ => "mods",
        };
        TitleText.Text = $"Modrinth · {instanceName}（MC {minecraftVersion} / {kindLabel}）";

        Loaded += async (_, _) =>
        {
            var query = Environment.GetEnvironmentVariable("ESLUNCHER_MODRINTH_QUERY");
            if (!string.IsNullOrWhiteSpace(query))
            {
                SearchBox.Text = query;
                await SearchAsync(query!);
            }
        };
    }

    private async void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        await SearchAsync(query);
    }

    private async Task SearchAsync(string query)
    {
        try
        {
            StatusText.Text = "搜索中 …";
            var hits = await _client.SearchAsync(query, _minecraftVersion, _loader, 20, CancellationToken.None, _kind);
            var items = await Task.WhenAll(hits.Select(async hit =>
                new ModrinthItem(
                    hit,
                    $"{hit.Downloads:N0} 下载 · {hit.Description}",
                    await IconCache.GetAsync(hit.IconUrl))));
            HitsList.ItemsSource = items;
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

        try
        {
            StatusText.Text = $"安装 {item.Hit.Title} …";
            var content = new InstanceContentService(_layout, new InstanceStore(_layout));
            var file = await _client.InstallAsync(content, _instance, item.Hit.Slug, _minecraftVersion, _loader, CancellationToken.None, _kind, _world);
            StatusText.Text = $"已安装 → {file}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"安装失败：{exception.Message}";
        }
    }
}

public sealed class ModrinthItem(ModrinthHit hit, string detail, Avalonia.Media.Imaging.Bitmap? icon = null)
{
    public ModrinthHit Hit { get; } = hit;

    public string Title => Hit.Title;

    public string Detail { get; } = detail;

    public Avalonia.Media.Imaging.Bitmap? Icon { get; } = icon;

    public bool HasIcon => Icon is not null;

    public bool HasNoIcon => Icon is null;

    public string Initial => string.IsNullOrEmpty(Title) ? "?" : Title[..1].ToUpperInvariant();

    public string DownloadsText => $"{Hit.Downloads:N0}";
}
