namespace GLauncher.Core.Metadata;

/// <summary>
/// 一条库依赖。新旧两代元数据并存：
/// 现代用 <c>downloads.artifact</c>（含 path/url/sha1/size）；
/// 早期版本可能只有 <c>url</c> + maven 名字，需要自己拼路径（见 <see cref="MavenCoordinate"/>）。
/// 旧版的 natives 用 <c>natives</c> 映射 + <c>classifiers</c>（含 ${arch} 模板）。
/// </summary>
public sealed record Library
{
    public static readonly IReadOnlyDictionary<string, DownloadArtifact> NoClassifiers =
        new Dictionary<string, DownloadArtifact>(0);

    public static readonly IReadOnlyDictionary<string, string> NoNatives =
        new Dictionary<string, string>(0);

    public required string Name { get; init; }

    public DownloadArtifact? Artifact { get; init; }

    public IReadOnlyDictionary<string, DownloadArtifact> Classifiers { get; init; } = NoClassifiers;

    /// <summary>os 名 → classifier 键（旧版 natives，值里可能含 <c>${arch}</c>）。</summary>
    public IReadOnlyDictionary<string, string> NativeClassifiers { get; init; } = NoNatives;

    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary><c>extract.exclude</c>（解压 natives 时跳过，通常是 <c>META-INF/</c>）。</summary>
    public IReadOnlyList<string> ExtractExclude { get; init; } = [];

    /// <summary>早期 maven 基址（<c>url</c>），与 <see cref="Artifact"/> 二选一。</summary>
    public string? BaseUrl { get; init; }

    public bool IsAllowed(LaunchEnvironment environment) => RuleEvaluator.IsAllowed(Rules, environment);

    /// <summary>
    /// 取出当前系统对应的 natives classifier 名（替换 <c>${arch}</c>）。
    /// 注意 <c>${arch}</c> 是 32/64 位（如 <c>natives-windows-64</c>），
    /// 与 rules 里的 <c>os.arch</c>（x86/x86_64）不是同一套取值。
    /// </summary>
    public string? ResolveNativeClassifier(string osName, string nativeArch)
    {
        if (!NativeClassifiers.TryGetValue(osName, out var template))
        {
            return null;
        }

        return template.Replace("${arch}", nativeArch, StringComparison.Ordinal);
    }
}
