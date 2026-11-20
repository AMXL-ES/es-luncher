using EsLuncher.Core.Downloads;

namespace EsLuncher.Core.Tests;

/// <summary>
/// 可编程的假网络：测试里用委托描述每个 URL 的行为，并记录所有请求（含 Range）。
/// </summary>
internal sealed class FakeRemoteFileClient : IRemoteFileClient
{
    private readonly List<(string Url, long RangeFrom)> _requests = [];

    public Func<Uri, long, CancellationToken, Task<RemoteFileResponse>>? OnGet { get; set; }

    public IReadOnlyList<(string Url, long RangeFrom)> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    public Task<RemoteFileResponse> GetAsync(Uri uri, long rangeFrom, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add((uri.AbsoluteUri, rangeFrom));
        }

        var handler = OnGet ?? throw new InvalidOperationException("测试未设置 OnGet");
        return handler(uri, rangeFrom, cancellationToken);
    }

    public static RemoteFileResponse NotFound() => new(404, null, null);

    public static RemoteFileResponse Ok(byte[] content) => new(200, new MemoryStream(content), content.Length);

    public static RemoteFileResponse Partial(byte[] contentFromOffset, long totalLength)
        => new(206, new MemoryStream(contentFromOffset), totalLength - (totalLength - contentFromOffset.Length));
}
