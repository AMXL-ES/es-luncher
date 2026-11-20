using Avalonia.Controls;
using Avalonia.Threading;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class LogWindow : Window
{
    private readonly string _logPath;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _lines = [];
    private long _position;

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

        try
        {
            var length = new FileInfo(_logPath).Length;
            if (length < _position)
            {
                // 日志被轮转/清空：重头再来
                _position = 0;
                _lines.Clear();
            }

            if (length > _position)
            {
                using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(_position, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    _lines.Add(line);
                }

                _position = stream.Position;
                if (_lines.Count > 3000)
                {
                    _lines.RemoveRange(0, _lines.Count - 3000);
                }

                UpdateText();
            }
        }
        catch (IOException exception)
        {
            StatusText.Text = $"读取日志失败：{exception.Message}";
        }
    }

    private void UpdateText()
    {
        var query = SearchBox.Text?.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? _lines
            : _lines.Where(line => line.Contains(query!, StringComparison.OrdinalIgnoreCase)).ToList();

        var text = string.Join('\n', visible.TakeLast(1500));
        if (!string.Equals(LogBox.Text, text, StringComparison.Ordinal))
        {
            LogBox.Text = text;
            if (AutoScrollBox.IsChecked == true)
            {
                LogBox.CaretIndex = text.Length;
            }
        }

        StatusText.Text = $"{visible.Count} 行 · {text.Length} 字符 · 已读 {_position / 1024.0:F1} KiB";
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => UpdateText();
}
