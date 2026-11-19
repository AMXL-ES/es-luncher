using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public class RuleEvaluatorTests
{
    [Fact]
    public void No_rules_means_allowed()
    {
        Assert.True(RuleEvaluator.IsAllowed([], TestEnvironment.WindowsX64()));
    }

    [Fact]
    public void Allow_windows_only_applies_on_windows()
    {
        Rule[] rules = [Allow(Os("windows"))];

        Assert.True(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX64()));
        Assert.False(RuleEvaluator.IsAllowed(rules, TestEnvironment.LinuxX64()));
    }

    [Fact]
    public void Allow_then_disallow_osx_matches_real_legacy_data()
    {
        // 1.12.2 的 lwjgl-platform 就是这个形状：[{"allow"},{"disallow","os":{"name":"osx"}}]
        Rule[] rules = [Allow(null), Disallow(Os("osx"))];

        Assert.True(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX64()));
        Assert.False(RuleEvaluator.IsAllowed(rules, TestEnvironment.MacArm64()));
    }

    [Fact]
    public void Arch_rule_matches_only_that_arch()
    {
        Rule[] rules = [Allow(OsConstraint.Create(null, "x86", null))];

        Assert.True(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX86()));
        Assert.False(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX64()));
    }

    [Fact]
    public void Os_version_is_a_regex()
    {
        Rule[] rules = [Allow(OsConstraint.Create(null, null, @"^10\."))];

        Assert.True(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX64()));

        var windows11 = new LaunchEnvironment("windows", "x86_64", "11.0.1", LaunchEnvironment.NoFeatures);
        Assert.False(RuleEvaluator.IsAllowed(rules, windows11));
    }

    [Fact]
    public void Feature_rule_requires_matching_feature()
    {
        var rule = new Rule
        {
            Action = RuleAction.Allow,
            Features = new Dictionary<string, bool> { ["is_demo_user"] = true },
        };

        Assert.False(RuleEvaluator.IsAllowed([rule], TestEnvironment.WindowsX64()));
        Assert.True(RuleEvaluator.IsAllowed([rule], TestEnvironment.WindowsX64(
            new Dictionary<string, bool> { ["is_demo_user"] = true })));
        Assert.False(RuleEvaluator.IsAllowed([rule], TestEnvironment.WindowsX64(
            new Dictionary<string, bool> { ["is_demo_user"] = false })));
    }

    [Fact]
    public void Later_matching_rule_wins()
    {
        Rule[] rules = [Allow(null), Disallow(Os("windows")), Allow(Os("windows"))];

        Assert.True(RuleEvaluator.IsAllowed(rules, TestEnvironment.WindowsX64()));
    }

    [Fact]
    public void Invalid_version_regex_is_rejected_at_parse_time()
    {
        Assert.Throws<MetadataFormatException>(() => OsConstraint.Create(null, null, "[unclosed"));
    }

    private static Rule Allow(OsConstraint? os) => new() { Action = RuleAction.Allow, Os = os };

    private static Rule Disallow(OsConstraint? os) => new() { Action = RuleAction.Disallow, Os = os };

    private static OsConstraint Os(string name) => OsConstraint.Create(name, null, null);
}
