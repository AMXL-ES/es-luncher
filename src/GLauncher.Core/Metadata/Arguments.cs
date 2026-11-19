namespace GLauncher.Core.Metadata;

/// <summary>
/// 新格式参数：<c>{"game":[...],"jvm":[...]}</c>。数组元素既可以是裸字符串，
/// 也可以是带 <c>rules</c> + <c>value</c> 的对象（例如 demo 模式、Quick Play）。
/// </summary>
public sealed record Arguments(IReadOnlyList<ArgumentEntry> Game, IReadOnlyList<ArgumentEntry> Jvm);

/// <param name="Rules">空 = 无条件生效。</param>
public sealed record ArgumentEntry(IReadOnlyList<Rule> Rules, IReadOnlyList<string> Values)
{
    public bool AppliesTo(LaunchEnvironment environment) => RuleEvaluator.IsAllowed(Rules, environment);
}
