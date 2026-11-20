namespace EsLuncher.Core.Auth;

public sealed record MinecraftProfile(string Id, string Name);

/// <summary>一次认证会话：启动器只把 token 交给游戏进程，绝不写进日志。</summary>
public sealed record AuthSession
{
    public required MinecraftProfile Profile { get; init; }

    public required string AccessToken { get; init; }

    /// <summary><c>msa</c>（微软正版）或 <c>legacy</c>（离线/皮肤站）。</summary>
    public string UserType { get; init; } = "legacy";

    public string? RefreshToken { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? Xuid { get; init; }

    public string Kind { get; init; } = "offline";

    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expires && expires <= now.AddMinutes(-2);

    /// <summary>给日志/UI 的安全摘要：永远不含 token。</summary>
    public string Describe() => $"{Profile.Name} ({Profile.Id}) via {Kind}, userType={UserType}, expires={ExpiresAt:O}";
}

public interface IAuthProvider
{
    string Kind { get; }

    Task<AuthSession> LoginAsync(CancellationToken cancellationToken);
}

/// <summary>离线身份：UUID 由名字推导，token 固定 "0"，只用于本地游玩。</summary>
public sealed class OfflineAuthProvider(string username) : IAuthProvider
{
    public string Kind => "offline";

    public Task<AuthSession> LoginAsync(CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var session = new AuthSession
        {
            Profile = new MinecraftProfile(Launching.OfflineIdentity.CreateUuid(username), username),
            AccessToken = "0",
            UserType = "legacy",
            Kind = "offline",
        };

        return Task.FromResult(session);
    }
}

public sealed class AuthenticationException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
