using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Metadata;

namespace EsLuncher.App;

public partial class DownloadsView : UserControl
{
    private VersionManifest? _manifest;
    private CancellationTokenSource? _cancellation;

    public DownloadsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadManifestAsync(refresh: false);
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
        ProgressBar.IsVisible = true;
        ProgressBar.Value = 0;

        try
        {
            using var client = new HttpRemoteFileClient();
            var downloader = new FileDownloader(client);
            var fetcher = new VersionFetcher(AppServices.Layout, downloader);
            var progress = new Progress<DownloadProgress>(value =>
            {
                var percent = value.FilesTotal == 0 ? 0 : value.FilesDone * 100.0 / value.FilesTotal;
                ProgressBar.Value = percent;
                StatusText.Text = $"{value.FilesDone}/{value.FilesTotal} 文件 · {value.BytesDownloaded / 1048576.0:F1} MiB · {value.CurrentPath}";
            });

            var report = await fetcher.FetchAsync(
                item.Id,
                new FetchOptions { Concurrency = 8 },
                progress,
                _cancellation.Token);

            StatusText.Text = report.Success
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
            CancelButton.IsEnabled = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _cancellation?.Cancel();
}

public sealed record VersionItem(string Id, string Type);
