using System.Text.Json;
using GLauncher.Core.Downloads;

namespace GLauncher.Core.Auth;

public interface ITokenStore
{
    Task SaveAsync(string accountKey, AuthSession session, CancellationToken cancellationToken);

    Task<AuthSession?> LoadAsync(string accountKey, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string accountKey, CancellationToken cancellationToken);

    IReadOnlyList<string> ListAccounts();
}

/// <summary>
/// DPAPI 加密的会话存储。<c>D:\mc-data\auth\accounts\&lt;name&gt;.bin</c>，当前用户才能解开。
/// 文件里没有明文 token，也不含密码。
/// </summary>
public sealed class DpapiTokenStore(DataLayout layout) : ITokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(string accountKey, AuthSession session, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(layout.AuthAccountsDir);
        var payload = JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions);
        var encrypted = Dpapi.Protect(payload);
        await File.WriteAllBytesAsync(PathFor(accountKey), encrypted, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthSession?> LoadAsync(string accountKey, CancellationToken cancellationToken)
    {
        var path = PathFor(accountKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var payload = Dpapi.Unprotect(encrypted);
        return JsonSerializer.Deserialize<AuthSession>(payload, JsonOptions);
    }

    public Task<bool> DeleteAsync(string accountKey, CancellationToken cancellationToken)
    {
        var path = PathFor(accountKey);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    public IReadOnlyList<string> ListAccounts()
    {
        if (!Directory.Exists(layout.AuthAccountsDir))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(layout.AuthAccountsDir, "*.bin")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string PathFor(string accountKey)
    {
        var safe = new string(accountKey.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        if (string.IsNullOrEmpty(safe))
        {
            throw new ArgumentException("账号键不能为空");
        }

        return Path.Combine(layout.AuthAccountsDir, safe + ".bin");
    }
}
