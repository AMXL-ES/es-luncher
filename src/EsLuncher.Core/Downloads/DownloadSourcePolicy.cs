namespace EsLuncher.Core.Downloads;

public enum DownloadSource
{
    Official,
    Bmclapi,
}

/// <summary>
/// 下载源重写：BMCLAPI 镜像只改写已知的官方主机；失败时由 <see cref="HttpRemoteFileClient"/> 回退官方。
/// </summary>
public static class DownloadSourcePolicy
{
    public static DownloadSource Source { get; set; } = DownloadSource.Official;

    public static Uri Rewrite(Uri uri)
    {
        if (Source != DownloadSource.Bmclapi)
        {
            return uri;
        }

        var host = uri.Host.ToLowerInvariant();
        var builder = new UriBuilder(uri);
        switch (host)
        {
            case "launchermeta.mojang.com":
            case "piston-meta.mojang.com":
            case "piston-data.mojang.com":
                builder.Host = "bmclapi2.bangbang93.com";
                return builder.Uri;
            case "libraries.minecraft.net":
                builder.Host = "bmclapi2.bangbang93.com";
                builder.Path = "/maven" + builder.Path;
                return builder.Uri;
            case "resources.download.minecraft.net":
                builder.Host = "bmclapi2.bangbang93.com";
                builder.Path = "/assets" + builder.Path;
                return builder.Uri;
            default:
                // launcher.mojang.com 等 mirror 结构不同，保持官方（客户端 jar 仍可下载）
                return uri;
        }
    }
}
