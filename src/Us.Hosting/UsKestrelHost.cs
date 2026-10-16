using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Us.Runtime;

namespace Us.Hosting;

/// <summary>
/// Thin Kestrel adapter over US route dispatch. No ASP.NET MVC/minimal-API surface for US programs.
/// </summary>
public sealed class UsKestrelHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly CancellationTokenSource _lifetime = new();

    private UsKestrelHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    /// <summary>
    /// Resolve listen URL: <c>US_URLS</c>, else <c>US_PORT</c> on loopback, else <c>http://127.0.0.1:5080</c>.
    /// Port <c>0</c> requests an ephemeral port (tests).
    /// </summary>
    public static string ResolveUrls()
    {
        var urls = Environment.GetEnvironmentVariable("US_URLS");
        if (!string.IsNullOrWhiteSpace(urls))
        {
            return urls.Trim();
        }

        var portText = Environment.GetEnvironmentVariable("US_PORT");
        if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out var port) && port >= 0)
        {
            return "http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return "http://127.0.0.1:5080";
    }

    /// <summary>Start Kestrel and return once listening. Caller must dispose/stop.</summary>
    public static async Task<UsKestrelHost> StartAsync(
        Func<UsHttpRequest, Task<UsHttpResponse>> dispatch,
        string? urls = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        var listen = string.IsNullOrWhiteSpace(urls) ? ResolveUrls() : urls.Trim();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = "US",
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls(listen);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
        });
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        });

        var app = builder.Build();
        app.Run(async context =>
        {
            try
            {
                var request = await AdaptRequestAsync(context.Request).ConfigureAwait(false);
                var response = await dispatch(request).ConfigureAwait(false);
                await WriteResponseAsync(context.Response, response).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("""{"hata":"sunucu hatası"}""", Encoding.UTF8).ConfigureAwait(false);
                }
            }
        });

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw new UsRuntimeException(
                UsRuntimeCodes.IoError,
                "HTTP dinleyici başlatılamadı: " + SummarizeBindFailure(exception),
                0,
                0);
        }

        var address = app.Urls.FirstOrDefault()
            ?? throw new UsRuntimeException(UsRuntimeCodes.IoError, "HTTP dinleyici adresi yok.", 0, 0);
        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseAddress))
        {
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
            throw new UsRuntimeException(UsRuntimeCodes.IoError, "HTTP dinleyici adresi geçersiz.", 0, 0);
        }

        return new UsKestrelHost(app, baseAddress);
    }

    /// <summary>Block until cancellation (Ctrl+C / token), then stop.</summary>
    public async Task RunUntilCancelledAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await StopAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort shutdown for tests.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    /// <summary>
    /// Production entry: listen when routes exist and test-route hook is inactive.
    /// Disabled with <c>US_KESTREL=0</c>.
    /// </summary>
    public static void MaybeListen()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("US_TEST_ROUTE")))
        {
            return;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("US_KESTREL"), "0", StringComparison.Ordinal))
        {
            return;
        }

        if (!UsEmitHttp.HasRoutes)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, args) =>
            {
                args.Cancel = true;
                cts.Cancel();
            };

            var host = StartAsync(UsEmitHttp.DispatchRequestAsync, cancellationToken: cts.Token)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            try
            {
                Console.Out.WriteLine("US dinliyor: " + host.BaseAddress.AbsoluteUri);
                Console.Out.Flush();
                host.RunUntilCancelledAsync(cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            finally
            {
                host.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (UsRuntimeException exception)
        {
            Console.Error.WriteLine(exception.Code + ": " + exception.Message);
            Environment.ExitCode = 1;
        }
    }

    public static async Task<UsHttpRequest> AdaptRequestAsync(HttpRequest request)
    {
        string? body = null;
        if (request.ContentLength is > 0 || request.Body.CanRead)
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (body.Length == 0)
            {
                body = null;
            }
        }

        var path = request.Path.HasValue ? request.Path.Value! : "/";
        var us = new UsHttpRequest(request.Method, path, body, request.ContentType);
        foreach (var header in request.Headers)
        {
            us.Headers[header.Key] = header.Value.ToString();
        }

        foreach (var pair in request.Query)
        {
            us.Query[pair.Key] = pair.Value.ToString();
        }

        return us;
    }

    public static async Task WriteResponseAsync(HttpResponse response, UsHttpResponse us)
    {
        response.StatusCode = us.StatusCode;
        if (!string.IsNullOrWhiteSpace(us.ContentType))
        {
            response.ContentType = us.ContentType;
        }

        foreach (var header in us.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers[header.Key] = header.Value;
        }

        if (us.Body is null)
        {
            return;
        }

        await response.WriteAsync(us.Body, Encoding.UTF8).ConfigureAwait(false);
    }

    private static string SummarizeBindFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is IOException or System.Net.Sockets.SocketException)
            {
                return current.Message;
            }
        }

        return exception.Message;
    }
}
