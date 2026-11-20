using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EsLuncher.Core.Auth;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class AccountsWindow : Window
{
    private readonly DataLayout _layout;
    private readonly DpapiTokenStore _store;
    private readonly InstanceConfig? _current;
    private CancelationHolder _cancel = new();

    /// <summary>设计器/XAML 加载用。</summary>
    public AccountsWindow()
        : this(AppServices.Layout, null)
    {
    }

    public AccountsWindow(DataLayout layout, InstanceConfig? current)
    {
        InitializeComponent();
        _layout = layout;
        _store = new DpapiTokenStore(layout);
        _current = current;
        HintText.Text = current is null ? "从「账户」进入时未指定实例" : $"「使用」会把账户绑定到实例：{current.Name}";
        Loaded += async (_, _) => await LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        var keys = _store.ListAccounts();
        var items = new List<AccountItem>();
        foreach (var key in keys)
        {
            string name = key;
            string kind = "offline";
            string? uuid = null;
            try
            {
                var session = await _store.LoadAsync(key, CancellationToken.None);
                if (session is not null)
                {
                    name = session.Profile.Name;
                    kind = session.Kind;
                    uuid = session.Profile.Id;
                }
            }
            catch (Exception)
            {
            }

            var avatar = uuid is null
                ? null
                : await IconCache.GetAsync($"https://crafatar.com/avatars/{uuid}?size=64&overlay");
            var state = _current?.AccountKey == key
                ? "当前实例"
                : AppServices.Settings.DefaultAccountKey == key ? "默认" : string.Empty;
            items.Add(new AccountItem(
                name,
                $"{kind} · {key}",
                state,
                key,
                kind,
                avatar));
        }

        if (items.Count == 0)
        {
            items.Add(new AccountItem("Player", "离线身份（未保存账户）", "默认", string.Empty, "offline", null));
        }

        AccountsList.ItemsSource = items;
        StatusText.Text = Localization.F("accounts.count", items.Count);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _cancel.Cancel();

    private async void OnUseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || string.IsNullOrEmpty(key))
        {
            StatusText.Text = Localization.T("accounts.noBindable");
            return;
        }

        if (_current is null)
        {
            StatusText.Text = Localization.T("accounts.fromInstance");
            return;
        }

        var session = await _store.LoadAsync(key, CancellationToken.None);
        var username = session?.Profile.Name ?? key;
        new InstanceStore(_layout).Save(_current with { AccountKey = key, Username = username });
        StatusText.Text = Localization.F("accounts.bound", username, _current.Name);
        _ = LoadAccountsAsync();
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || string.IsNullOrEmpty(key))
        {
            return;
        }

        await _store.DeleteAsync(key, CancellationToken.None);
        StatusText.Text = Localization.F("accounts.deleted", key);
        _ = LoadAccountsAsync();
    }

    private void OnSetDefaultClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || string.IsNullOrEmpty(key))
        {
            return;
        }

        AppServices.SaveSettings(AppServices.Settings with { DefaultAccountKey = key });
        StatusText.Text = Localization.F("accounts.setDefaultDone", key);
        _ = LoadAccountsAsync();
    }

    private async void OnRefreshAvatarsClick(object? sender, RoutedEventArgs e)
    {
        IconCache.ClearAll();
        await LoadAccountsAsync();
        StatusText.Text = Localization.T("accounts.avatarsRefreshed");
    }

    private async void OnAddOfflineClick(object? sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            StatusText.Text = Localization.T("accounts.usernameRequired");
            return;
        }

        try
        {
            var session = await new OfflineAuthProvider(username).LoginAsync(CancellationToken.None);
            await _store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None);
            UsernameBox.Text = string.Empty;
            StatusText.Text = Localization.F("accounts.savedOffline", session.Profile.Name);
            _ = LoadAccountsAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = Localization.F("accounts.saveFailed", exception.Message);
        }
    }

    private async void OnMicrosoftLoginClick(object? sender, RoutedEventArgs e)
    {
        var clientId = Environment.GetEnvironmentVariable("ESLUNCHER_MSA_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("ESLUNCHER_MS_CLIENT_ID");
        if (string.IsNullOrWhiteSpace(clientId))
        {
            StatusText.Text = "微软登录需要 Azure 公共客户端 ID：设置环境变量 ESLUNCHER_MSA_CLIENT_ID 后重开";
            return;
        }

        _cancel = new CancelationHolder();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var client = new MicrosoftAuthClient(http, clientId);
            var session = await client.LoginAsync(
                flow => Dispatcher.UIThread.Post(() =>
                    StatusText.Text = $"浏览器打开 {flow.VerificationUri}，输入代码 {flow.UserCode}（等待中…）"),
                _cancel.Token);
            await _store.SaveAsync(session.Profile.Name.ToLowerInvariant(), session, CancellationToken.None);
            StatusText.Text = $"已保存微软账户：{session.Profile.Name}";
            _ = LoadAccountsAsync();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消微软登录";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"微软登录失败：{exception.Message}";
        }
    }

    private sealed class CancelationHolder
    {
        private CancellationTokenSource _source = new();

        public CancellationToken Token => _source.Token;

        public void Cancel()
        {
            _source.Cancel();
        }
    }
}

public sealed record AccountItem(string Name, string Detail, string State, string Key, string Kind, Avalonia.Media.Imaging.Bitmap? Avatar)
{
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    public bool HasAvatar => Avatar is not null;

    public bool HasNoAvatar => Avatar is null;

    public string KindLabel => Kind switch
    {
        "microsoft" => "微软",
        "offline" => "离线",
        _ => Kind,
    };
}
