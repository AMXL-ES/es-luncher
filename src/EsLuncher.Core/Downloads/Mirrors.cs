namespace EsLuncher.Core.Downloads;

/// <summary>
/// 镜像改写规则。全部经过 2026-11-19 实测：BMCLAPI 的 maven / assets / version client 端点
/// 都返回 206（支持 Range），可直接当回退源。
/// </summary>
public static class Mirrors
{
    public const string BmclapiBase = "https://bmclapi2.bangbang93.com";

    public static Uri? ForLibrary(Uri official)
    {
        if (!string.Equals(official.Host, "libraries.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Uri(BmclapiBase + "/maven" + official.AbsolutePath);
    }

    public static Uri? ForAsset(Uri official)
    {
        if (!string.Equals(official.Host, "resources.download.minecraft.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Uri(BmclapiBase + "/assets" + official.AbsolutePath);
    }

    /// <summary>客户端 jar：官方 URL 里没有版本号，但 BMCLAPI 有按版本号直达的端点。</summary>
    public static Uri ForClientJar(string versionId)
        => new($"{BmclapiBase}/version/{Uri.EscapeDataString(versionId)}/client");

    public static IReadOnlyList<Uri> Collect(params Uri?[] candidates)
    {
        var list = new List<Uri>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (candidate is not null && !list.Contains(candidate))
            {
                list.Add(candidate);
            }
        }

        return list;
    }
}
