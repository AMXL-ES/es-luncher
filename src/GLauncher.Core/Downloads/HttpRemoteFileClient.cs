using System.Net;
using System.Net.Http.Headers;

namespace GLauncher.Core.Downloads;

/// <summary>真实网络实现。<c>HttpClient.Timeout</c> 设为无限：大文件下载不能被整体超时打断。</summary>
public sealed class HttpRemoteFileClient : IRemoteFileClient, IDisposable
{
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public HttpRemoteFileClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(15),
            };
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }
    }

    public async Task<RemoteFileResponse> GetAsync(Uri uri, long rangeFrom, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (rangeFrom > 0)
        {
            request.Headers.Range = new RangeHeaderValue(rangeFrom, null);
        }

        // 只对"拿到响应头"这一段设超时；正文读取交给调用方的 CancellationToken。
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerCts.CancelAfter(HeaderTimeout);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"请求超时（{HeaderTimeout.TotalSeconds:0}s 内未拿到响应头）: {uri}");
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return new RemoteFileResponse((int)response.StatusCode, null, null);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new RemoteFileResponse(
            (int)response.StatusCode,
            new ResponseOwnedStream(response, stream),
            response.Content.Headers.ContentLength);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>让"释放内容流"同时释放 HttpResponseMessage（否则连接会挂到 GC）。</summary>
    private sealed class ResponseOwnedStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
