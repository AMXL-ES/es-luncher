using Avalonia.Controls;
using Avalonia.Threading;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class LogWindow : Window
{
    private readonly string _logPath;
    private readonly DispatcherTimer _timer;

    /// <summary>设计器/XAML 加载用。</summary>
    public LogWindow()
        : this("(未选择实例)", AppServices.Layout)
    {
    }

    public LogWindow(string instanceName, DataLayout layout)
    {
        InitializeComponent();
        var instance = new InstanceStore(layout).TryLoad(instanceName);
        _logPath = instance is null
            ? string.Empty
            : Path.Combine(layout.GameDir(instance.Name), "logs", "latest.log");
        Title = $"日志 · {instanceName}";
        TitleText.Text = $"日志 · {instanceName}";
        PathText.Text = _logPath;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void Refresh()
    {
        if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath))
        {
            StatusText.Text = "没有 latest.log（游戏还没启动过）";
            return;
        }

        var query = SearchBox.Text?.Trim();
        var lines = File.ReadLines(_logPath).TakeLast(1200);
        if (!string.IsNullOrWhiteSpace(query))
        {
            lines = lines.Where(line => line.Contains(query!, StringComparison.OrdinalIgnoreCase));
        }

        var text = string.Join('\n', lines);
        LogBox.Text = text;
        StatusText.Text = $"{text.Length} 字符";
        if (AutoScrollBox.IsChecked == true)
        {
            LogBox.CaretIndex = text.Length;
        }
    }
}
