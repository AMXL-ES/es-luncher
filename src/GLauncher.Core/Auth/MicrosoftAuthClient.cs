using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace GLauncher.Core.Auth;

public sealed record DeviceCodeFlow(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    string Message,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>
/// 微软设备码登录全链路：设备码 → MSA token → XBL → XSTS → Minecraft login → profile。
/// 只做官方公开接口，不伪造任何身份；clientId 必须是用户自己在 Azure 注册的公共客户端应用。
/// </summary>
public sealed class MicrosoftAuthClient
{
    public const string DeviceCodeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
    public const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    public const string XboxUserAuthEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    public const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    public const string MinecraftLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    public const string MinecraftProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public MicrosoftAuthClient(HttpClient http, string clientId, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        _http = http;
        _clientId = clientId;
        _delay = delay ?? ((time, ct) => Task.Delay(time, ct));
    }

    public async Task<DeviceCodeFlow> RequestDeviceCodeAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = "XboxLive.signin offline_access",
        };

        var (status, body) = await PostFormAsync(DeviceCodeEndpoint, form, cancellationToken).ConfigureAwait(false);
        var json = TryParse(body);
        if (status is < 200 or >= 300 || json?["device_code"] is null)
        {
            throw new AuthenticationException($"设备码请求失败：HTTP {status} {body}", "device_code_failed");
        }

        return new DeviceCodeFlow(
            DeviceCode: (string)json["device_code"]!,
            UserCode: (string)json["user_code"]!,
            VerificationUri: (string)json["verification_uri"]!,
            Message: (string?)json["message"] ?? string.Empty,
            ExpiresInSeconds: (int?)json["expires_in"] ?? 900,
            IntervalSeconds: (int?)json["interval"] ?? 5);
    }

    public async Task<AuthSession> LoginAsync(Action<DeviceCodeFlow>? onDeviceCode, CancellationToken cancellationToken)
    {
        var deviceCode = await RequestDeviceCodeAsync(cancellationToken).ConfigureAwait(false);
        onDeviceCode?.Invoke(deviceCode);

        var microsoft = await PollForMicrosoftTokenAsync(deviceCode, cancellationToken).ConfigureAwait(false);
        return await CompleteLoginAsync(microsoft.AccessToken, microsoft.RefreshToken, microsoft.ExpiresInSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = "XboxLive.signin offline_access",
        };

        var (status, body) = await PostFormAsync(TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
        var json = TryParse(body);
        if (status is < 200 or >= 300 || json?["access_token"] is null)
        {
            throw new AuthenticationException(
                $"刷新 token 失败：HTTP {status} {(string?)json?["error"] ?? body}",
                (string?)json?["error"]);
        }

        return await CompleteLoginAsync(
                (string)json["access_token"]!,
                (string?)json["refresh_token"] ?? refreshToken,
                (int?)json["expires_in"] ?? 3600,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MinecraftProfile> FetchProfileAsync(string minecraftAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MinecraftProfileEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", minecraftAccessToken);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new AuthenticationException("Minecraft token 已失效（401），需要重新登录", "unauthorized");
        }

        var json = TryParse(body);
        if (!response.IsSuccessStatusCode || json?["id"] is null)
        {
            throw new AuthenticationException($"读取玩家档案失败：HTTP {(int)response.StatusCode}", "profile_failed");
        }

        return new MinecraftProfile((string)json["id"]!, (string)json["name"]!);
    }

    private async Task<AuthSession> CompleteLoginAsync(
        string microsoftAccessToken,
        string refreshToken,
        int microsoftExpiresInSeconds,
        CancellationToken cancellationToken)
    {
        var xblToken = await XboxUserAuthenticateAsync(microsoftAccessToken, cancellationToken).ConfigureAwait(false);
        var (xstsToken, userHash) = await XstsAuthorizeAsync(xblToken, cancellationToken).ConfigureAwait(false);
        var (minecraftToken, minecraftExpiresIn) = await MinecraftLoginAsync(xstsToken, userHash, cancellationToken)
            .ConfigureAwait(false);
        var profile = await FetchProfileAsync(minecraftToken, cancellationToken).ConfigureAwait(false);

        return new AuthSession
        {
            Profile = profile,
            AccessToken = minecraftToken,
            UserType = "msa",
            RefreshToken = refreshToken,
            Xuid = userHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(minecraftExpiresIn > 0 ? minecraftExpiresIn : microsoftExpiresInSeconds),
            Kind = "microsoft",
        };
    }

    private async Task<(string AccessToken, string RefreshToken, int ExpiresInSeconds)> PollForMicrosoftTokenAsync(
        DeviceCodeFlow deviceCode,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, deviceCode.IntervalSeconds));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresInSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await _delay(interval, cancellationToken).ConfigureAwait(false);

            var form = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode.DeviceCode,
            };

            var (status, body) = await PostFormAsync(TokenEndpoint, form, cancellationToken).ConfigureAwait(false);
            var json = TryParse(body);

            if (status is >= 200 and < 300 && json?["access_token"] is not null)
            {
                return ((string)json["access_token"]!, (string?)json["refresh_token"] ?? string.Empty, (int?)json["expires_in"] ?? 3600);
            }

            switch ((string?)json?["error"])
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "expired_token":
                    throw new AuthenticationException("设备码已过期，请重新登录", "expired_token");
                case "authorization_declined":
                    throw new AuthenticationException("你在浏览器里拒绝了授权", "authorization_declined");
                case "bad_verification_code":
                    throw new AuthenticationException("设备码无效", "bad_verification_code");
                default:
                    throw new AuthenticationException(
                        $"微软登录失败：{(string?)json?["error"] ?? $"HTTP {status}"} {(string?)json?["error_description"]}",
                        (string?)json?["error"]);
            }
        }

        throw new AuthenticationException("等待授权超时，请重新登录", "expired_token");
    }

    private async Task<string> XboxUserAuthenticateAsync(string microsoftAccessToken, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["AuthMethod"] = "RPS",
                ["SiteName"] = "user.auth.xboxlive.com",
                ["RpsTicket"] = $"d={microsoftAccessToken}",
            },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT",
        };

        var (status, body) = await PostJsonAsync(XboxUserAuthEndpoint, payload, cancellationToken).ConfigureAwait(false);
        var json = TryParse(body);
        if (status is < 200 or >= 300 || json?["Token"] is null)
        {
            throw new AuthenticationException($"Xbox Live 认证失败：HTTP {status}", "xbl_failed");
        }

        return (string)json["Token"]!;
    }

    private async Task<(string Token, string UserHash)> XstsAuthorizeAsync(string xblToken, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["Properties"] = new JsonObject
            {
                ["UserTokens"] = new JsonArray(xblToken),
                ["SandboxId"] = "RETAIL",
            },
            ["RelyingParty"] = "rp://api.minecraftservices.com/",
            ["TokenType"] = "JWT",
        };

        var (status, body) = await PostJsonAsync(XstsEndpoint, payload, cancellationToken).ConfigureAwait(false);
        var json = TryParse(body);
        if (status is < 200 or >= 300)
        {
            var errorCode = json?["XErr"]?.GetValue<long>() ?? 0;
            throw new AuthenticationException(MapXstsError(errorCode), errorCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var token = (string)json!["Token"]!;
        var userHash = (string)json["DisplayClaims"]!["xui"]![0]!["uhs"]!;
        return (token, userHash);
    }

    private async Task<(string AccessToken, int ExpiresInSeconds)> MinecraftLoginAsync(
        string xstsToken,
        string userHash,
        CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["identityToken"] = $"XBL3.0 x={userHash};{xstsToken}",
        };

        var (status, body) = await PostJsonAsync(MinecraftLoginEndpoint, payload, cancellationToken).ConfigureAwait(false);
        var json = TryParse(body);
        if (status is < 200 or >= 300 || json?["access_token"] is null)
        {
            throw new AuthenticationException(
                $"Minecraft 登录失败：HTTP {status} {(string?)json?["errorMessage"] ?? (string?)json?["error"]}",
                "minecraft_login_failed");
        }

        return ((string)json["access_token"]!, (int?)json["expires_in"] ?? 0);
    }

    private static string MapXstsError(long errorCode) => errorCode switch
    {
        2148916233 => "这个微软账号还没有 Xbox 档案：先到 xbox.com 登录一次创建档案",
        2148916235 => "该地区无法使用 Xbox Live（地区限制）",
        2148916236 or 2148916237 => "需要成人验证（账号年龄限制）",
        2148916238 => "儿童账号：需要家长在 microsoft.com/family 里把你加入家庭组",
        _ => $"XSTS 授权失败（错误码 {errorCode}）",
    };

    private async Task<(int Status, string Body)> PostFormAsync(
        string uri,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .PostAsync(uri, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task<(int Status, string Body)> PostJsonAsync(
        string uri,
        JsonNode payload,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private static JsonNode? TryParse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
