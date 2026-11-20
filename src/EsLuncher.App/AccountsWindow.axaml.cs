using Avalonia.Controls;
using EsLuncher.Core.Auth;
using EsLuncher.Core.Downloads;
using EsLuncher.Core.Instances;

namespace EsLuncher.App;

public partial class AccountsWindow : Window
{
    /// <summary>设计器/XAML 加载用。</summary>
    public AccountsWindow()
        : this(AppServices.Layout, null)
    {
    }

    public AccountsWindow(DataLayout layout, InstanceConfig? current)
    {
        InitializeComponent();
        var store = new DpapiTokenStore(layout);
        var keys = store.ListAccounts();
        var items = new List<AccountItem>();

        foreach (var key in keys)
        {
            string name = key;
            string kind = "offline";
            try
            {
                var session = store.LoadAsync(key, CancellationToken.None).GetAwaiter().GetResult();
                if (session is not null)
                {
                    name = session.Profile.Name;
                    kind = session.Kind;
                }
            }
            catch (Exception)
            {
                // 读不出来就显示 key
            }

            items.Add(new AccountItem(
                name,
                $"{kind} · {key}",
                current?.AccountKey == key ? "当前实例" : string.Empty));
        }

        if (items.Count == 0)
        {
            items.Add(new AccountItem("Player", "离线身份（未保存账户）", "默认"));
        }

        AccountsList.ItemsSource = items;
        StatusText.Text = $"共 {items.Count} 个账户 · CLI：esluncher login --provider offline --username <名字>";
    }
}

public sealed record AccountItem(string Name, string Detail, string State)
{
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();
}
