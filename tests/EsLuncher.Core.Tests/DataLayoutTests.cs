using EsLuncher.Core.Downloads;
using Xunit;

namespace EsLuncher.Core.Tests;

public sealed class DataLayoutDefaultRootTests
{
    [Fact]
    public void Env_variable_overrides_default_root()
    {
        var old = Environment.GetEnvironmentVariable("ESLUNCHER_DATA");
        try
        {
            Environment.SetEnvironmentVariable("ESLUNCHER_DATA", @"X:\custom-data");
            Assert.Equal(@"X:\custom-data", DataLayout.DefaultRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESLUNCHER_DATA", old);
        }
    }
}
