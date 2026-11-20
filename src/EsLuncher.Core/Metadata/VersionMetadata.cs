namespace EsLuncher.Core.Metadata;

/// <summary>
/// 一个 version.json。原版用 <see cref="Arguments"/>（1.13+）或
/// <see cref="LegacyMinecraftArguments"/>（≤1.12.2），两者至少有一个。
/// </summary>
public sealed record VersionMetadata
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string MainClass { get; init; }

    /// <summary>加载器叠加用（原版为 null）。</summary>
    public string? InheritsFrom { get; init; }

    public string? Assets { get; init; }

    public AssetIndex? AssetIndex { get; init; }

    public JavaVersionInfo? JavaVersion { get; init; }

    public string? LegacyMinecraftArguments { get; init; }

    public Arguments? Arguments { get; init; }

    public IReadOnlyList<Library> Libraries { get; init; } = [];

    public DownloadArtifact? Client { get; init; }

    public DownloadArtifact? Server { get; init; }

    public LoggingInfo? Logging { get; init; }

    public int? MinimumLauncherVersion { get; init; }

    public int? ComplianceLevel { get; init; }

    public DateTimeOffset? ReleaseTime { get; init; }
}

/// <summary>资源索引。597 KB 一次下载，之后按哈希前 2 位分目录。</summary>
public sealed record AssetIndex
{
    public string? Id { get; init; }

    public required string Url { get; init; }

    public required string Sha1 { get; init; }

    public long? Size { get; init; }

    public long? TotalSize { get; init; }
}

/// <param name="MajorVersion">选 JDK 用的主版本（26.3 → 25，1.7.10 → 8）。</param>
public sealed record JavaVersionInfo(string? Component, int MajorVersion);

/// <summary>log4j 配置文件：下载 <see cref="File"/>，启动时把 <see cref="Argument"/> 里的 ${path} 换成它。</summary>
public sealed record LoggingInfo(string Argument, DownloadArtifact File, string? Type);
