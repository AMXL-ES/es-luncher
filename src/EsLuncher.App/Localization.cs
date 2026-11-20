namespace EsLuncher.App;

/// <summary>
/// 极简 i18n：键 → (中文, English)。语言切换在设置里保存，重启生效。
/// 目前覆盖主导航、实例页工具栏与搜索占位；新增词条只需加一行。
/// </summary>
public static class Localization
{
    public static string Language { get; set; } = "zh";

    private static readonly Dictionary<string, (string Zh, string En)> Strings = new()
    {
        ["title"] = ("ES Luncher", "ES Luncher"),
        ["nav.home"] = ("主页", "Home"),
        ["nav.instances"] = ("实例", "Instances"),
        ["nav.downloads"] = ("下载", "Downloads"),
        ["nav.others"] = ("其他", "Others"),
        ["nav.settings"] = ("设置", "Settings"),
        ["nav.about"] = ("关于", "About"),
        ["toolbar.refresh"] = ("刷新", "Refresh"),
        ["toolbar.launch"] = ("启动", "Launch"),
        ["toolbar.open"] = ("打开目录", "Open folder"),
        ["toolbar.diagnostics"] = ("诊断", "Diagnostics"),
        ["toolbar.modrinth"] = ("Modrinth", "Modrinth"),
        ["toolbar.log"] = ("日志", "Logs"),
        ["search.placeholder"] = ("搜索实例（名字/版本）", "Search instances (name/version)"),
    };

    public static string T(string key)
        => Strings.TryGetValue(key, out var pair) ? (Language == "en" ? pair.En : pair.Zh) : key;
}
