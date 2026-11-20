using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EsLuncher.Core.Diagnostics;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class DiagnosticsWindow : Window
{
    private readonly string _instance;
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
}
