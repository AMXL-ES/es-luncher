using System.Reflection;

namespace EsLuncher.Core.Tests;

/// <summary>
/// 架构守卫：宪章第 1 条（引擎零 UI 依赖）必须由机器强制，而不是靠 reviewer 的记忆。
/// 这条测试一旦变红，说明有人把 UI 类型引进 Core 了 —— 那是不可逆的架构腐化。
/// </summary>
public class ArchitectureGuardTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Avalonia",
        "System.Windows",
        "PresentationFramework",
        "PresentationCore",
        "Microsoft.Win32.Registry",
    ];

    [Fact]
    public void Core_must_not_reference_any_ui_framework()
    {
        var core = Assembly.Load("EsLuncher.Core");
        var referenced = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        var offenders = referenced
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"EsLuncher.Core 引用了 UI/平台框架：{string.Join(", ", offenders)}。引擎层必须保持零 UI 依赖。");
    }

    [Fact]
    public void Core_assembly_is_loadable_and_non_empty()
    {
        var core = Assembly.Load("EsLuncher.Core");

        Assert.Equal("EsLuncher.Core", core.GetName().Name);
        Assert.NotNull(core.GetName().Version);
    }
}
