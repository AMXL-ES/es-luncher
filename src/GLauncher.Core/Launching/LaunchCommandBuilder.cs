using GLauncher.Core.Downloads;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Launching;

public sealed record LaunchRequest
{
    public required string Username { get; init; }

    public string? Uuid { get; init; }

    public string? AccessToken { get; init; }

    public string? InstanceId { get; init; }

    public int MemoryMb { get; init; } = 2048;

    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];

    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];

    public string LauncherName { get; init; } = "GLauncher";

    public string LauncherVersion { get; init; } = "0.1.0-dev";
}

/// <summary>可直接交给 <see cref="ProcessRunner.RunLoggedAsync"/> 的启动命令。</summary>
public sealed record LaunchCommand(
    string JavaExePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string Classpath,
    string NativesDirectory,
    string AssetsDirectory);

/// <summary>
/// 组装 javaw/java 的完整命令行。新旧两代参数格式都由 <see cref="ArgumentAssembler"/> 处理；
/// 这里负责 JVM 参数、日志配置、classpath、主类、工作目录。
/// </summary>
public static class LaunchCommandBuilder
{
    public static LaunchCommand Build(
        VersionMetadata version,
        LaunchEnvironment environment,
        DataLayout layout,
        JavaRuntime java,
        LaunchRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Username);

        var instanceId = request.InstanceId ?? version.Id;
        var gameDirectory = layout.GameDir(instanceId);
        var nativesDirectory = layout.NativeDir(instanceId);
        var assetsIndex = version.Assets ?? version.AssetIndex?.Id ?? version.Id;
        var classpath = string.Join(Path.PathSeparator, ClasspathBuilder.Build(version, environment, layout));

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = request.Username,
            ["version_name"] = version.Id,
            ["game_directory"] = gameDirectory,
            ["assets_root"] = layout.AssetsDir,
            ["assets_index_name"] = assetsIndex,
            ["auth_uuid"] = request.Uuid ?? OfflineIdentity.CreateUuid(request.Username),
            ["auth_access_token"] = request.AccessToken ?? "0",
            ["user_type"] = request.AccessToken is null ? "legacy" : "msa",
            ["version_type"] = version.Type,
            ["natives_directory"] = nativesDirectory,
            ["classpath"] = classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = layout.LibrariesDir,
            ["launcher_name"] = request.LauncherName,
            ["launcher_version"] = request.LauncherVersion,
            // 离线模式下的空值：真实客户端会接受空串，缺失则会抛占位符异常。
            ["clientid"] = string.Empty,
            ["auth_xuid"] = string.Empty,
            ["user_properties"] = "{}",
        };

        var arguments = new List<string>
        {
            $"-Xmx{request.MemoryMb}m",
        };
        arguments.AddRange(request.ExtraJvmArguments);

        if (version.Arguments is not null)
        {
            arguments.AddRange(ArgumentAssembler.BuildJvmArguments(version, environment, values));
        }
        else
        {
            // 旧格式（≤1.12.2）的 JVM 参数在 version.json 里没有，只能启动器自己拼。
            arguments.Add($"-Djava.library.path={nativesDirectory}");
        }

        if (version.Logging is { File: { } logFile } logging)
        {
            var fileName = Path.GetFileName(new Uri(logFile.Url).AbsolutePath);
            var logPath = Path.Combine(layout.AssetsDir, "log_configs", fileName);
            arguments.Add(logging.Argument.Replace("${path}", logPath, StringComparison.Ordinal));
        }

        arguments.Add("-cp");
        arguments.Add(classpath);
        arguments.Add(version.MainClass);

        arguments.AddRange(ArgumentAssembler.BuildGameArguments(version, environment, values));
        arguments.AddRange(request.ExtraGameArguments);

        return new LaunchCommand(
            java.JavaExePath,
            arguments,
            gameDirectory,
            classpath,
            nativesDirectory,
            layout.AssetsDir);
    }
}
