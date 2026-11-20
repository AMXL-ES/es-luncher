using EsLuncher.Core.Launching;

namespace EsLuncher.Core.Tests;

public class JavaVersionParserTests
{
    [Theory]
    [InlineData("openjdk version \"17.0.12\" 2024-07-16 LTS\nOpenJDK Runtime Environment (build 17.0.12+7-LTS)", 17)]
    [InlineData("java version \"1.8.0_431\"\nJava(TM) SE Runtime Environment (build 1.8.0_431-b10)", 8)]
    [InlineData("openjdk version \"21.0.13.0.2\" 2026-10-22 LTS", 21)]
    [InlineData("java version \"11.0.22\" 2024-01-16 LTS", 11)]
    public void Parses_major_version_from_real_output(string output, int expected)
    {
        Assert.Equal(expected, JavaVersionParser.TryParseMajor(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a java output")]
    public void Returns_null_when_nothing_parseable(string output)
    {
        Assert.Null(JavaVersionParser.TryParseMajor(output));
    }
}

public class JavaSelectorTests
{
    private static JavaRuntime Runtime(int major) => new($"C:\\java{major}\\bin\\java.exe", major, $"version {major}");

    [Fact]
    public void Picks_exact_major_version()
    {
        var selection = JavaSelector.Select(17, [Runtime(21), Runtime(17)]);

        Assert.True(selection.IsSatisfied);
        Assert.Equal(17, selection.Runtime!.MajorVersion);
    }

    [Fact]
    public void Explains_missing_runtime_instead_of_guessing()
    {
        var selection = JavaSelector.Select(8, [Runtime(17), Runtime(21)]);

        Assert.False(selection.IsSatisfied);
        Assert.Contains("需要 Java 8", selection.Message);
        Assert.Contains("Java 17", selection.Message);
        Assert.Contains("Java 21", selection.Message);
    }

    [Fact]
    public void Respects_explicit_override_with_warning()
    {
        var selection = JavaSelector.Select(8, [], Runtime(21));

        Assert.True(selection.IsSatisfied);
        Assert.Equal(21, selection.Runtime!.MajorVersion);
        Assert.Contains("警告", selection.Message);
    }

    [Fact]
    public void Empty_discovery_says_so()
    {
        var selection = JavaSelector.Select(17, []);

        Assert.False(selection.IsSatisfied);
        Assert.Contains("未发现任何可用 Java", selection.Message);
        Assert.Contains("PATH", selection.Message);
    }
}

public class OfflineIdentityTests
{
    [Fact]
    public void Uuid_is_deterministic_and_well_formed()
    {
        var first = OfflineIdentity.CreateUuid("Dev");
        var second = OfflineIdentity.CreateUuid("Dev");

        Assert.Equal(first, second);
        Assert.Equal(32, first.Length);
        Assert.Equal('3', first[12]);           // version 3
        Assert.Contains(first[16], new[] { '8', '9', 'a', 'b' });     // RFC 4122 variant
        Assert.All(first, ch => Assert.True(char.IsAsciiHexDigit(ch)));
    }

    [Fact]
    public void Different_names_get_different_uuids()
    {
        Assert.NotEqual(OfflineIdentity.CreateUuid("Dev"), OfflineIdentity.CreateUuid("Steve"));
    }
}
