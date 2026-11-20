using EsLuncher.Core.Launching;
using EsLuncher.Core.Metadata;

namespace EsLuncher.Core.Tests;

public class ArgumentAssemblerTests
{
    private static VersionMetadata Modern() => VersionMetadataParser.Parse(TestFixtures.Load("26.3.min.json"));

    private static Dictionary<string, string> Values() => new()
    {
        ["auth_player_name"] = "Steve",
        ["version_name"] = "26.3",
        ["game_directory"] = @"C:\Users\HP\.esluncher\instances\26.3",
        ["assets_root"] = @"D:\mc-data\assets",
        ["assets_index_name"] = "34",
        ["natives_directory"] = @"D:\mc-data\instances\26.3\natives",
        ["auth_uuid"] = "00000000000000000000000000000000",
        ["auth_access_token"] = "token",
        ["user_type"] = "msa",
        ["version_type"] = "release",
    };

    [Fact]
    public void Modern_game_arguments_expand_placeholders()
    {
        var arguments = ArgumentAssembler.BuildGameArguments(Modern(), TestEnvironment.WindowsX64(), Values());

        Assert.Equal("--username", arguments[0]);
        Assert.Equal("Steve", arguments[1]);
        Assert.Contains("--gameDir", arguments);
        Assert.Contains(@"C:\Users\HP\.esluncher\instances\26.3", arguments);
        Assert.DoesNotContain("--demo", arguments);
        Assert.DoesNotContain(arguments, value => value.Contains("${", StringComparison.Ordinal));
    }

    [Fact]
    public void Demo_flag_appears_only_when_feature_is_set()
    {
        var demoEnvironment = TestEnvironment.WindowsX64(new Dictionary<string, bool> { ["is_demo_user"] = true });

        var arguments = ArgumentAssembler.BuildGameArguments(Modern(), demoEnvironment, Values());

        Assert.Contains("--demo", arguments);
    }

    [Fact]
    public void Jvm_arguments_respect_os_and_arch_rules()
    {
        var windows = ArgumentAssembler.BuildJvmArguments(Modern(), TestEnvironment.WindowsX64(), Values());
        Assert.Contains(@"-Djava.library.path=D:\mc-data\instances\26.3\natives", windows);
        Assert.DoesNotContain("-XstartOnFirstThread", windows);
        Assert.DoesNotContain("-Xss1M", windows);

        var mac = ArgumentAssembler.BuildJvmArguments(Modern(), TestEnvironment.MacArm64(), Values());
        Assert.Contains("-XstartOnFirstThread", mac);

        var x86 = ArgumentAssembler.BuildJvmArguments(Modern(), TestEnvironment.WindowsX86(), Values());
        Assert.Contains("-Xss1M", x86);
        Assert.Contains("-XX:-UseBASE64Intrinsics", x86);
    }

    [Fact]
    public void Legacy_template_expands_in_order()
    {
        var version = VersionMetadataParser.Parse(TestFixtures.Load("1.7.10.min.json"));

        var arguments = ArgumentAssembler.BuildGameArguments(version, TestEnvironment.WindowsX64(), Values());

        Assert.Equal("--username", arguments[0]);
        Assert.Equal("Steve", arguments[1]);
        Assert.Contains("--accessToken", arguments);
        Assert.Contains("token", arguments);
        Assert.DoesNotContain(arguments, value => value.Contains("${", StringComparison.Ordinal));
    }

    [Fact]
    public void Quoted_legacy_placeholder_keeps_spaces()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","libraries":[],
         "minecraftArguments":"--gameDir \"${game_directory}\" --demo"}
        """;
        var version = VersionMetadataParser.Parse(json);
        var values = new Dictionary<string, string> { ["game_directory"] = @"C:\Users\HP\My Games" };

        var arguments = ArgumentAssembler.BuildGameArguments(version, TestEnvironment.WindowsX64(), values);

        Assert.Equal(3, arguments.Count);
        Assert.Equal("--gameDir", arguments[0]);
        Assert.Equal(@"C:\Users\HP\My Games", arguments[1]);
        Assert.Equal("--demo", arguments[2]);
    }

    [Fact]
    public void Missing_placeholder_throws_with_its_name()
    {
        var exception = Assert.Throws<LaunchPlaceholderException>(() =>
            ArgumentAssembler.BuildGameArguments(Modern(), TestEnvironment.WindowsX64(), new Dictionary<string, string>()));

        Assert.Equal("auth_player_name", exception.Placeholder);
    }

    [Fact]
    public void Version_without_any_argument_block_is_rejected()
    {
        const string json = """{"id":"x","type":"release","mainClass":"m","libraries":[]}""";
        var version = VersionMetadataParser.Parse(json);

        Assert.Throws<MetadataFormatException>(() =>
            ArgumentAssembler.BuildGameArguments(version, TestEnvironment.WindowsX64(), new Dictionary<string, string>()));
    }

    [Fact]
    public void Unbalanced_quote_in_legacy_template_is_rejected()
    {
        const string json = """
        {"id":"x","type":"release","mainClass":"m","libraries":[],
         "minecraftArguments":"--gameDir \"unterminated"}
        """;
        var version = VersionMetadataParser.Parse(json);

        Assert.Throws<MetadataFormatException>(() =>
            ArgumentAssembler.BuildGameArguments(version, TestEnvironment.WindowsX64(), new Dictionary<string, string>()));
    }
}
