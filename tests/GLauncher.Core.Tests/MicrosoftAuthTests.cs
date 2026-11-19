using System.Net;
using GLauncher.Core.Auth;

namespace GLauncher.Core.Tests;

public sealed class MicrosoftAuthTests
{
    private const string ClientId = "test-client-id";

    private static readonly string DeviceCodeJson = """
    {"device_code":"dc-1","user_code":"UC-123","verification_uri":"https://microsoft.com/devicelogin",
     "message":"open the page","expires_in":900,"interval":1}
    """;

    private static readonly string TokenPendingJson = """{"error":"authorization_pending"}""";
    private static readonly string TokenSuccessJson = """{"access_token":"msa-token","refresh_token":"refresh-token","expires_in":3600}""";

    private static FakeHttpHandler BaseRoutes() => new FakeHttpHandler()
        .Route(MicrosoftAuthClient.DeviceCodeEndpoint, DeviceCodeJson)
        .Route(MicrosoftAuthClient.XboxUserAuthEndpoint, """{"Token":"xbl-token"}""")
        .Route(MicrosoftAuthClient.XstsEndpoint, """{"Token":"xsts-token","DisplayClaims":{"xui":[{"uhs":"uhs-123"}]}}""")
        .Route(MicrosoftAuthClient.MinecraftLoginEndpoint, """{"access_token":"mc-token","expires_in":86400}""")
        .Route(MicrosoftAuthClient.MinecraftProfileEndpoint, """{"id":"uuid-1","name":"Steve"}""");

    [Fact]
    public async Task Full_device_code_chain_produces_msa_session()
    {
        var handler = BaseRoutes().RouteSequence(
            MicrosoftAuthClient.TokenEndpoint,
            (TokenPendingJson, HttpStatusCode.BadRequest),
            (TokenSuccessJson, HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var client = new MicrosoftAuthClient(http, ClientId, (time, _) =>
        {
            delays.Add(time);
            return Task.CompletedTask;
        });

        DeviceCodeFlow? shown = null;
        var session = await client.LoginAsync(device => shown = device, CancellationToken.None);

        Assert.NotNull(shown);
        Assert.Equal("UC-123", shown!.UserCode);
        Assert.Equal("https://microsoft.com/devicelogin", shown.VerificationUri);

        Assert.Equal("Steve", session.Profile.Name);
        Assert.Equal("uuid-1", session.Profile.Id);
        Assert.Equal("mc-token", session.AccessToken);
        Assert.Equal("refresh-token", session.RefreshToken);
        Assert.Equal("uhs-123", session.Xuid);
        Assert.Equal("msa", session.UserType);
        Assert.Equal("microsoft", session.Kind);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);

        Assert.Equal(2, delays.Count); // 第一次 pending，第二次成功
        Assert.Contains(handler.Requests, request => request.Url == MicrosoftAuthClient.MinecraftLoginEndpoint);
        Assert.Contains(handler.Requests, request => request.Body.Contains("client_id=test-client-id", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Body.Contains("identityToken", StringComparison.Ordinal) || request.Body.Contains("XBL3.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Slow_down_increases_poll_interval()
    {
        var handler = BaseRoutes().RouteSequence(
            MicrosoftAuthClient.TokenEndpoint,
            (TokenPendingJson, HttpStatusCode.BadRequest),
            ("""{"error":"slow_down"}""", HttpStatusCode.BadRequest),
            (TokenSuccessJson, HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var delays = new List<TimeSpan>();
        var client = new MicrosoftAuthClient(http, ClientId, (time, _) =>
        {
            delays.Add(time);
            return Task.CompletedTask;
        });

        await client.LoginAsync(null, CancellationToken.None);

        Assert.Equal(3, delays.Count);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[1]);
        Assert.Equal(TimeSpan.FromSeconds(6), delays[2]); // slow_down 后 +5s
    }

    [Fact]
    public async Task Xsts_error_is_mapped_to_human_message()
    {
        var handler = BaseRoutes().Route(
            MicrosoftAuthClient.XstsEndpoint,
            """{"XErr":2148916233}""",
            HttpStatusCode.Unauthorized);
        handler.RouteSequence(MicrosoftAuthClient.TokenEndpoint, (TokenSuccessJson, HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new MicrosoftAuthClient(http, ClientId, (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => client.LoginAsync(null, CancellationToken.None));

        Assert.Contains("Xbox 档案", exception.Message, StringComparison.Ordinal);
        Assert.Equal("2148916233", exception.Code);
    }

    [Fact]
    public async Task Refresh_uses_refresh_token_grant_and_keeps_session()
    {
        var handler = BaseRoutes().RouteSequence(MicrosoftAuthClient.TokenEndpoint, (TokenSuccessJson, HttpStatusCode.OK));
        using var http = new HttpClient(handler);
        var client = new MicrosoftAuthClient(http, ClientId, (_, _) => Task.CompletedTask);

        var session = await client.RefreshAsync("old-refresh-token", CancellationToken.None);

        Assert.Equal("mc-token", session.AccessToken);
        Assert.Equal("msa", session.UserType);
        Assert.Contains(
            handler.Requests,
            request => request.Url == MicrosoftAuthClient.TokenEndpoint
                && request.Body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                && request.Body.Contains("refresh_token=old-refresh-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_device_code_throws_clear_error()
    {
        var handler = BaseRoutes().RouteSequence(
            MicrosoftAuthClient.TokenEndpoint,
            ("""{"error":"expired_token"}""", HttpStatusCode.BadRequest));
        using var http = new HttpClient(handler);
        var client = new MicrosoftAuthClient(http, ClientId, (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => client.LoginAsync(null, CancellationToken.None));

        Assert.Contains("设备码已过期", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_profile_reports_relogin_needed()
    {
        var handler = BaseRoutes().Route(
            MicrosoftAuthClient.MinecraftProfileEndpoint,
            """{"error":"unauthorized"}""",
            HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var client = new MicrosoftAuthClient(http, ClientId, (_, _) => Task.CompletedTask);

        var exception = await Assert.ThrowsAsync<AuthenticationException>(() => client.FetchProfileAsync("bad-token", CancellationToken.None));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
    }
}
