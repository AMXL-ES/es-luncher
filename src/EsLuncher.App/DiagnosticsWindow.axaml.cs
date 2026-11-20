using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EsLuncher.Core.Content;
using EsLuncher.Core.Diagnostics;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class DiagnosticsWindow : Window
{
    private readonly string _instance;
    private readonly DataLayout _layout;
    private readonly InstanceDiagnostics _diagnostics;

    /// <summary>设计器/XAML 加载用的无参构造。</summary>
    public DiagnosticsWindow()
        : this("(未选择实例)", AppServices.Layout)
    {
    }

    public DiagnosticsWindow(string instanceName, DataLayout layout)
    {
        InitializeComponent();
        _instance = instanceName;
        _layout = layout;
        _diagnostics = new InstanceDiagnostics(layout, new InstanceStore(layout));
        Loaded += (_, _) => Analyze();
    }

    private void Analyze()
    {
        try
        {
            var report = _diagnostics.Analyze(_instance);
            TitleText.Text = $"诊断 · {_instance}";
            LogText.Text = report.LogPath ?? "(没有 latest.log)";
            ErrorsList.ItemsSource = report.Errors.Count == 0 ? new[] { "(没有错误行)" } : report.Errors;
            CrashesList.ItemsSource = report.Crashes.Select(crash => crash.Summary).ToArray();
            HintsList.ItemsSource = report.Hints.Count == 0 ? new[] { "(没有额外提示)" } : report.Hints;
        }
        catch (Exception exception)
        {
            TitleText.Text = $"诊断 · {_instance}";
            HintsList.ItemsSource = new[] { $"无法诊断：{exception.Message}" };
        }
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出诊断包",
            SuggestedFileName = $"{_instance}-diagnostics.zip",
            DefaultExtension = "zip",
        });
        if (file is null)
        {
            return;
        }

        try
        {
            var path = _diagnostics.Export(_instance, file.Path.LocalPath);
            StatusText.Text = $"已导出：{path}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"导出失败：{exception.Message}";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnFixDependenciesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var store = new InstanceStore(_layout);
            var instance = store.TryLoad(_instance)
                ?? throw new InvalidOperationException($"实例不存在：{_instance}");
            var content = new InstanceContentService(_layout, store);
            var checker = new DependencyChecker(content);
            var missing = checker.Check(instance.Name).Missing;
            if (missing.Count == 0)
            {
                StatusText.Text = "没有检测到缺失前置";
                return;
            }

            var (minecraftVersion, loader) = InstanceVersionInfo.Derive(instance.VersionId);
            var modrinthLoader = loader == "fabric-loader" ? "fabric" : loader == "quilt-loader" ? "quilt" : loader;
            using var client = new HttpRemoteFileClient();
            var modrinth = new ModrinthClient(client);

            foreach (var dependency in missing)
            {
                StatusText.Text = $"补装 {dependency} …";
                var hits = await modrinth.SearchAsync(dependency, minecraftVersion, modrinthLoader, 5, CancellationToken.None);
                var hit = hits.FirstOrDefault(candidate =>
                    string.Equals(candidate.Slug, dependency, StringComparison.OrdinalIgnoreCase));
                if (hit is null)
                {
                    StatusText.Text = $"跳过 {dependency}：Modrinth 没有同名项目";
                    continue;
                }

                await modrinth.InstallAsync(content, instance.Name, hit.Slug, minecraftVersion, modrinthLoader, CancellationToken.None);
                StatusText.Text = $"已补装 {dependency}";
            }

            var after = checker.Check(instance.Name);
            StatusText.Text = after.Missing.Count == 0
                ? "补齐完成：依赖满足（静态检查）"
                : $"仍可能缺：{string.Join(", ", after.Missing)}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"补装失败：{exception.Message}";
        }
    }
}
