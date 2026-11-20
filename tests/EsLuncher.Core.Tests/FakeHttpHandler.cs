using System.Net;
using System.Text;

namespace EsLuncher.Core.Tests;

/// <summary>按 URL 路由的假 HTTP：可记录请求体，可返回任意状态码。</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
    private readonly List<(string Url, string Body)> _requests = [];

    public IReadOnlyList<(string Url, string Body)> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    public FakeHttpHandler Route(string url, Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _routes[url] = handler;
        return this;
    }

    public FakeHttpHandler Route(string url, string body, HttpStatusCode status = HttpStatusCode.OK)
        => Route(url, _ => Task.FromResult(Json(body, status)));

    public FakeHttpHandler RouteSequence(string url, params (string Body, HttpStatusCode Status)[] responses)
    {
        var index = 0;
        return Route(url, _ =>
        {
            var captured = responses[Math.Min(index++, responses.Length - 1)];
            return Task.FromResult(Json(captured.Body, captured.Status));
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        lock (_requests)
        {
            _requests.Add((url, body));
        }

        if (_routes.TryGetValue(url, out var handler))
        {
            return await handler(request).ConfigureAwait(false);
        }

        return Json("{}", HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
