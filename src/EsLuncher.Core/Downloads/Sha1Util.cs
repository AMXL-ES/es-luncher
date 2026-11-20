using System.Security.Cryptography;

namespace EsLuncher.Core.Downloads;

public static class Sha1Util
{
    public static async Task<string> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> MatchesAsync(string path, string expectedSha1, CancellationToken cancellationToken)
    {
        var actual = await ComputeAsync(path, cancellationToken);
        return string.Equals(actual, expectedSha1, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>资源对象的哈希必须是 40 位十六进制，否则拒绝（防路径穿越与脏数据）。</summary>
    public static bool IsValidSha1(string? value)
    {
        if (value is null || value.Length != 40)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
