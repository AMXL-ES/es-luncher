namespace GLauncher.Core.Metadata;

/// <summary>
/// maven 坐标：<c>group:artifact:version[:classifier][@ext]</c>。
/// 用于两种场景：解析 <c>libraries[].name</c>，以及在只有 <c>url</c> 的旧元数据里拼下载路径。
/// </summary>
public sealed record MavenCoordinate(string Group, string Artifact, string Version, string? Classifier, string Extension)
{
    public static MavenCoordinate Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new MetadataFormatException("$.libraries[].name", "库名不能为空");
        }

        var extension = "jar";
        var core = name;
        var at = name.LastIndexOf('@');
        if (at > 0)
        {
            extension = name[(at + 1)..];
            core = name[..at];
        }

        var parts = core.Split(':');
        if (parts.Length is < 3 or > 4)
        {
            throw new MetadataFormatException("$.libraries[].name", $"不是合法的 maven 坐标：'{name}'");
        }

        var classifier = parts.Length == 4 ? parts[3] : null;
        return new MavenCoordinate(parts[0], parts[1], parts[2], classifier, extension);
    }

    /// <summary>拼出相对 maven 根目录的路径（正斜杠）。</summary>
    public string ToMavenPath(string? classifier = null)
    {
        var effective = classifier ?? Classifier;
        var file = $"{Artifact}-{Version}{(effective is null ? string.Empty : $"-{effective}")}.{Extension}";
        return $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{file}";
    }
}
