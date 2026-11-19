namespace GLauncher.Core.Metadata;

/// <summary>
/// 版本元数据格式错误。外部输入（manifest / version.json）都是敌意数据，
/// 出错时必须带上 JSON 路径，否则排障只能靠猜。
/// </summary>
public sealed class MetadataFormatException(string path, string message)
    : Exception($"版本元数据格式错误 @ {path}: {message}")
{
    public string Path { get; } = path;
}

/// <summary>
/// 启动参数里的 <c>${name}</c> 占位符没有对应值。
/// 这是"启动器自己的 bug"，不是外部数据格式错误，所以单独一个异常类型。
/// </summary>
public sealed class LaunchPlaceholderException(string placeholder, string context)
    : Exception($"启动参数占位符 ${{{placeholder}}} 没有值（出现在 {context}）")
{
    public string Placeholder { get; } = placeholder;
}
