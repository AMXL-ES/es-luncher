using System.Runtime.InteropServices;

namespace EsLuncher.App;

/// <summary>关于页「第三方组件」的一行。</summary>
public sealed record AboutComponentItem(string Name, string Version, string License);

/// <summary>关于页「数据与服务」的一行。</summary>
public sealed record AboutServiceItem(string Name, string Usage);

/// <summary>
/// 关于页的静态清单：本项目真正用到的组件与外部服务。
/// 版本号能在运行时读到的就现读（避免文档漂移），读不到的（仅测试用的包）写死并在改依赖时同步。
/// </summary>
public static class AboutInfo
{
    public static IReadOnlyList<AboutComponentItem> BuildComponents()
    {
        var avalonia = typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3) ?? "12.x";
        var velopack = typeof(Velopack.VelopackApp).Assembly.GetName().Version?.ToString(3) ?? "1.x";
        var dotnet = Environment.Version.ToString();

        return new[]
        {
            new AboutComponentItem("Avalonia（Desktop / Themes.Fluent）", avalonia, "MIT"),
            new AboutComponentItem(".NET 运行时", dotnet, "MIT"),
            new AboutComponentItem("Velopack（安装与自动更新）", velopack, "MIT"),
            new AboutComponentItem("xunit（仅测试，不随产品分发）", "2.9.3", "Apache-2.0"),
            new AboutComponentItem("Microsoft.NET.Test.Sdk（仅测试）", "17.14.1", "MIT"),
            new AboutComponentItem("coverlet.collector（仅测试）", "6.0.4", "MIT"),
        };
    }

    public static IReadOnlyList<AboutServiceItem> Services { get; } = new[]
    {
        new AboutServiceItem("Mojang 版本清单与资源", "游戏版本、库、资源索引、客户端 jar：官方下载并逐个校验 SHA1"),
        new AboutServiceItem("Mojang Java 运行时", "按版本元数据自动安装所需的 Java（jre-legacy / java-runtime-*）"),
        new AboutServiceItem("Microsoft 账号 OAuth", "设备码登录；需要自备 Azure 公共客户端 ID，本项目不内置任何第三方 ID"),
        new AboutServiceItem("Modrinth API v2", "模组 / 资源包 / 光影 / 数据包 / 整合包的检索与安装"),
        new AboutServiceItem("BMCLAPI 镜像", "官方源不可用时的下载回退（默认仍走官方）"),
        new AboutServiceItem("Crafatar", "账户皮肤头像；取不到时回退为名字首字母"),
        new AboutServiceItem("Fabric / Quilt Meta", "加载器版本列表与 profile JSON"),
        new AboutServiceItem("Forge / NeoForge 官方安装器", "用官方 installer 安装加载器"),
    };

    public static string RuntimeInfo()
    {
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        return $".NET {Environment.Version} · {architecture}";
    }
}
