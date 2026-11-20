using Avalonia.Markup.Xaml;

namespace EsLuncher.App;

/// <summary>XAML 里用 <c>{app:T key}</c> 取本地化文案（重启生效）。</summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Localization.T(Key);
}
