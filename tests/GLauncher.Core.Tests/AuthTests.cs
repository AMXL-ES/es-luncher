using GLauncher.Core.Auth;
using GLauncher.Core.Downloads;
using GLauncher.Core.Launching;

namespace GLauncher.Core.Tests;

public sealed class OfflineAuthTests
{
    [Fact]
    public async Task Offline_session_matches_offline_uuid_rule()
    {
        var session = await new OfflineAuthProvider("Dev").LoginAsync(CancellationToken.None);

        Assert.Equal(OfflineIdentity.CreateUuid("Dev"), session.Profile.Id);
        Assert.Equal("Dev", session.Profile.Name);
        Assert.Equal("0", session.AccessToken);
        Assert.Equal("legacy", session.UserType);
        Assert.Equal("offline", session.Kind);
        Assert.False(session.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Empty_username_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new OfflineAuthProvider(" ").LoginAsync(CancellationToken.None));
    }

    [Fact]
    public void Describe_never_contains_token()
    {
        var session = new AuthSession
        {
            Profile = new MinecraftProfile("id", "Steve"),
            AccessToken = "super-secret-token",
        };

        Assert.DoesNotContain("super-secret-token", session.Describe(), StringComparison.Ordinal);
    }
}

public sealed class TokenStoreTests : IDisposable
{
    private readonly string _root;
    private readonly DataLayout _layout;

    public TokenStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "glauncher-auth-tests", Guid.NewGuid().ToString("N"));
        _layout = new DataLayout(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Dpapi_roundtrip_and_ciphertext_differs()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var plaintext = "token:abc123"u8.ToArray();

        var ciphertext = Dpapi.Protect(plaintext);
        var restored = Dpapi.Unprotect(ciphertext);

        Assert.Equal(plaintext, restored);
        Assert.NotEqual(plaintext, ciphertext);
    }

    [Fact]
    public async Task Store_encrypts_on_disk_and_loads_back()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new DpapiTokenStore(_layout);
        var session = new AuthSession
        {
            Profile = new MinecraftProfile("uuid-1", "Steve"),
            AccessToken = "mc-access-token",
            RefreshToken = "ms-refresh-token",
            UserType = "msa",
            Kind = "microsoft",
        };

        await store.SaveAsync("steve", session, CancellationToken.None);

        var file = Path.Combine(_layout.AuthAccountsDir, "steve.bin");
        Assert.True(File.Exists(file));
        var onDisk = await File.ReadAllBytesAsync(file);
        var onDiskText = System.Text.Encoding.UTF8.GetString(onDisk);
        Assert.DoesNotContain("mc-access-token", onDiskText, StringComparison.Ordinal);

        var loaded = await store.LoadAsync("steve", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("mc-access-token", loaded!.AccessToken);
        Assert.Equal("Steve", loaded.Profile.Name);

        Assert.Contains("steve", store.ListAccounts());

        Assert.True(await store.DeleteAsync("steve", CancellationToken.None));
        Assert.Empty(store.ListAccounts());
    }

    [Fact]
    public async Task Missing_account_returns_null()
    {
        var store = new DpapiTokenStore(_layout);

        Assert.Null(await store.LoadAsync("nobody", CancellationToken.None));
    }
}
