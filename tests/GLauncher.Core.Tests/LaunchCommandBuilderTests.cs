using GLauncher.Core.Downloads;
using GLauncher.Core.Launching;
using GLauncher.Core.Metadata;

namespace GLauncher.Core.Tests;

public sealed class LaunchCommandBuilderTests
{
    private readonly DataLayout _layout = new(Path.Combine(Path.GetTempPath(), "glauncher-launch-tests"));

    private static JavaRuntime Java(int major) => new($@"C:\java{major}\bin\java.exe", major, $"version {major}");

    [Fact]
    public void Legacy_version_builds_full_command()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.7.10.min.json"));

        var command = LaunchCommandBuilder.Build(
            version,
            TestEnvironment.WindowsX64(),
            _layout,
            Java(8),
            new LaunchRequest { Username = "Dev", MemoryMb = 1024 });

        Assert.Equal(@"C:\java8\bin\java.exe", command.JavaExePath);
        Assert.Equal(_layout.GameDir("1.7.10"), command.WorkingDirectory);
        Assert.Equal(_layout.NativeDir("1.7.10"), command.NativesDirectory);
        Assert.Equal("-Xmx1024m", command.Arguments[0]);
        Assert.Contains($"-Djava.library.path={_layout.NativeDir("1.7.10")}", command.Arguments);

        var arguments = command.Arguments.ToList();
        var cpIndex = arguments.IndexOf("-cp");
        Assert.True(cpIndex > 0);
        Assert.Equal(command.Classpath, arguments[cpIndex + 1]);
        Assert.Equal("net.minecraft.client.main.Main", arguments[cpIndex + 2]);

        Assert.Contains("--username", arguments);
        Assert.Contains("Dev", arguments);
        Assert.Contains("--assetIndex", arguments);
        Assert.Contains("1.7.10", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("${", StringComparison.Ordinal));
    }

    [Fact]
    public void Modern_version_uses_metadata_jvm_arguments_and_logging()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

        var command = LaunchCommandBuilder.Build(
            version,
            TestEnvironment.WindowsX64(),
            _layout,
            Java(25),
            new LaunchRequest { Username = "Steve" });

        Assert.Equal(1, command.Arguments.Count(argument => argument.StartsWith("-Djava.library.path=", StringComparison.Ordinal)));
        Assert.Contains(command.Arguments, argument => argument.StartsWith("-Dlog4j.configurationFile=", StringComparison.Ordinal));
        Assert.DoesNotContain("--demo", command.Arguments);
        Assert.Contains("Steve", command.Arguments);
        Assert.EndsWith("brigadier-1.3.10.jar", command.Classpath.Split(Path.PathSeparator)[0]);
    }

    [Fact]
    public void Offline_uuid_is_injected_when_not_provided()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.7.10.min.json"));

        var command = LaunchCommandBuilder.Build(
            version,
            TestEnvironment.WindowsX64(),
            _layout,
            Java(8),
            new LaunchRequest { Username = "Dev" });

        var arguments = command.Arguments.ToList();
        var uuidIndex = arguments.IndexOf("--uuid");
        Assert.True(uuidIndex > 0);
        var uuid = arguments[uuidIndex + 1];

        Assert.Equal(OfflineIdentity.CreateUuid("Dev"), uuid);
        Assert.DoesNotContain("${", uuid, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_defaults_cover_clientid_xuid_and_user_properties()
    {
        // 1.18.2 真实客户端参数里有 --clientId ${clientid} / --xuid ${auth_xuid}；
        // 1.7.10 有 --userProperties ${user_properties}。缺一个就会在启动前抛占位符异常。
        const string json = """
        {"id":"x","type":"release","mainClass":"m","libraries":[],
         "minecraftArguments":"--gameDir ${game_directory} --clientId ${clientid} --xuid ${auth_xuid} --userProperties ${user_properties}"}
        """;
        var version = VersionMetadataParser.Parse(json);

        var command = LaunchCommandBuilder.Build(
            version,
            TestEnvironment.WindowsX64(),
            _layout,
            Java(17),
            new LaunchRequest { Username = "Dev" });

        var arguments = command.Arguments.ToList();
        Assert.Contains("--clientId", arguments);
        Assert.Contains("--userProperties", arguments);
        Assert.Equal("{}", arguments[arguments.IndexOf("--userProperties") + 1]);
        Assert.DoesNotContain(arguments, argument => argument.Contains("${", StringComparison.Ordinal));
    }
}
