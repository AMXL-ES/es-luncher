namespace GLauncher.Core.Downloads;

/// <summary>
/// 一个可释放的下载响应。<see cref="StatusCode"/> 为 200 表示整文件，
/// 206 表示服务端接受了 Range（可续传）。
/// </summary>
public sealed class RemoteFileResponse : IDisposable
{
    public RemoteFileResponse(int statusCode, Stream? content, long? contentLength)
    {
        StatusCode = statusCode;
        Content = content;
        ContentLength = contentLength;
    }

    public int StatusCode { get; }

    public Stream? Content { get; }

    public long? ContentLength { get; }

    public bool IsSuccess => Content is not null && StatusCode is 200 or 206;

    public void Dispose() => Content?.Dispose();
}

/// <summary>下载器的网络缝：测试注入假实现，生产走 <see cref="HttpRemoteFileClient"/>。</summary>
public interface IRemoteFileClient
{
    Task<RemoteFileResponse> GetAsync(Uri uri, long rangeFrom, CancellationToken cancellationToken);
}
