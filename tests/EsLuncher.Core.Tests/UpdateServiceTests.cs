using EsLuncher.Core.Updates;
using Xunit;

namespace EsLuncher.Core.Tests;

/// <summary>
/// 更新服务的契约测试：先检查、后下载/应用。这两条不需要真实的 Velopack 安装，
/// 因为参数校验发生在碰到 UpdateManager 之前。
/// </summary>
public sealed class UpdateServiceTests
{
    [Fact]
    public async Task Download_without_check_is_rejected()
    {
        var service = new VelopackUpdateService(@"D:\es-luncher-no-such-update-source");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
    }

    [Fact]
    public void Apply_without_check_is_rejected()
    {
        var service = new VelopackUpdateService(@"D:\es-luncher-no-such-update-source");

        Assert.Throws<InvalidOperationException>(() => service.ApplyAndRestart());
    }
}
