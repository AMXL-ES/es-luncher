using System.Security.Cryptography;
using System.Text;

namespace GLauncher.Core.Launching;

/// <summary>
/// 离线身份。UUID 用 Mojang 的离线规则：<c>MD5("OfflinePlayer:" + name)</c> 并置为 v3。
/// 只用于本地离线游玩，绝不用于伪造正版身份。
/// </summary>
public static class OfflineIdentity
{
    public static string CreateUuid(string username)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);

        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant RFC 4122
        return Convert.ToHexStringLower(hash);
    }
}
