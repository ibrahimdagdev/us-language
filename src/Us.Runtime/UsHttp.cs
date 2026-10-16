namespace Us.Runtime;

public sealed class UsHttpRequest
{
    public UsHttpRequest(string method, string path, string? body = null, string? contentType = null)
    {
        Method = method;
        Path = path;
        Body = body;
        ContentType = contentType;
    }

    public string Method { get; }

    public string Path { get; }

    public string? Body { get; }

    public string? ContentType { get; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Query { get; } = new(StringComparer.Ordinal);
}

public sealed class UsHttpResponse
{
    public UsHttpResponse(int statusCode, string? body = null, string? contentType = null)
    {
        StatusCode = statusCode;
        Body = body;
        ContentType = contentType;
    }

    public int StatusCode { get; init; }

    public string? Body { get; init; }

    public string? ContentType { get; init; }

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UsHttpResult
{
    public UsHttpResult(int statusCode, UsValue body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    public int StatusCode { get; }

    public UsValue Body { get; }
}

public sealed class UsHttpClientResponse
{
    public UsHttpClientResponse(int statusCode, string body, string? contentType, IReadOnlyDictionary<string, string> headers)
    {
        StatusCode = statusCode;
        Body = body;
        ContentType = contentType;
        Headers = headers;
    }

    public int StatusCode { get; }

    public string Body { get; }

    public string? ContentType { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }
}

public sealed class UsHttpClientBuilder
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IHttpTransport
{
    ValueTask<UsHttpClientResponse> SendAsync(string method, string url, string? body, string? contentType, IReadOnlyDictionary<string, string> headers);
}

public sealed class FakeHttpTransport : IHttpTransport
{
    private readonly Dictionary<string, UsHttpClientResponse> _responses = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string>? LastHeaders { get; private set; }

    public void Register(string method, string url, UsHttpClientResponse response) =>
        _responses[method + " " + url] = response;

    public ValueTask<UsHttpClientResponse> SendAsync(string method, string url, string? body, string? contentType, IReadOnlyDictionary<string, string> headers)
    {
        LastHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        if (_responses.TryGetValue(method + " " + url, out var response))
        {
            return ValueTask.FromResult(response);
        }

        throw new UsRuntimeException(UsRuntimeCodes.UnknownTransportPath, $"'{method} {url}' taşıma yolu kayıtlı değil.", 0, 0);
    }
}

/// <summary>
/// Production outbound transport. Reuses a shared <see cref="HttpClient"/> to avoid socket exhaustion.
/// Fake remains the default for tests via <see cref="UsEmitHost.Transport"/>.
/// </summary>
public sealed class HttpClientTransport : IHttpTransport
{
    private static readonly HttpClient Shared = CreateClient();

    private readonly HttpClient _client;

    public HttpClientTransport()
        : this(Shared)
    {
    }

    public HttpClientTransport(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    public async ValueTask<UsHttpClientResponse> SendAsync(
        string method,
        string url,
        string? body,
        string? contentType,
        IReadOnlyDictionary<string, string> headers)
    {
        using var message = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            message.Content = new StringContent(body, System.Text.Encoding.UTF8);
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                message.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            }
        }

        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                if (string.Equals(pair.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!message.Headers.TryAddWithoutValidation(pair.Key, pair.Value) && message.Content is not null)
                {
                    message.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }
            }
        }

        using var response = await _client.SendAsync(message).ConfigureAwait(false);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
        {
            responseHeaders[header.Key] = string.Join(", ", header.Value);
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
        }

        var responseContentType = response.Content?.Headers.ContentType?.ToString();
        return new UsHttpClientResponse((int)response.StatusCode, responseBody, responseContentType, responseHeaders);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("US/0.1");
        return client;
    }
}
