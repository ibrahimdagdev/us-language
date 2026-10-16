using System.Globalization;
using System.Text.Json;

namespace Us.Runtime;

/// <summary>
/// Outbound HTTP + route dispatch helpers for emitted IL.
/// In-process via <see cref="DispatchAsync"/>; real listen via Us.Hosting Kestrel adapter.
/// Uses <see cref="UsEmitHost.Transport"/> for outbound calls.
/// </summary>
public static class UsEmitHttp
{
    /// <summary>Set by Us.Hosting when loaded; production Kestrel entry.</summary>
    public static Action? ProductionListen { get; set; }

    private sealed class RouteEntry
    {
        public required string Method { get; init; }

        public required string Template { get; init; }

        public required string[] Segments { get; init; }

        public required bool[] IsParameter { get; init; }

        public required string[] ParameterNames { get; init; }

        public required string[] ParameterKinds { get; init; }

        public required string[] ParameterTypeNames { get; init; }

        public required string[] BodyShapes { get; init; }

        public required string[] SetupServices { get; init; }

        public required bool Async { get; init; }

        public Func<UsValue[], UsValue>? SyncHandler { get; init; }

        public Func<UsValue[], Task<UsValue>>? AsyncHandler { get; init; }

        public int Specificity { get; init; }
    }

    private static readonly List<RouteEntry> Routes = [];
    private static readonly object Sync = new();

    public static bool HasRoutes
    {
        get
        {
            lock (Sync)
            {
                return Routes.Count > 0;
            }
        }
    }

    public static void ResetRoutes()
    {
        lock (Sync)
        {
            Routes.Clear();
        }
    }

    public static UsValue Header(UsValue receiver, UsValue name, UsValue value, int start, int length)
    {
        var header = name.Text ?? string.Empty;
        if (header.Length == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.EmptyHeaderName, "Başlık adı boş olamaz.", start, length);
        }

        var builder = receiver.Reference as UsHttpClientBuilder ?? new UsHttpClientBuilder();
        builder.Headers[header] = value.Text ?? string.Empty;
        return UsValue.FromReference(builder);
    }

    public static UsValue Send(UsValue receiver, string method, UsValue url, UsValue body, int start, int length) =>
        UsEmitAsync.Box(SendAsync(receiver, method, url, body, start, length));

    public static async Task<UsValue> SendAsync(UsValue receiver, string method, UsValue url, UsValue body, int start, int length)
    {
        _ = start;
        _ = length;
        var target = url.Text ?? string.Empty;
        string? payload = null;
        string? contentType = null;
        if (!body.IsNull && body.Kind != UsValueKind.Unit)
        {
            if (body.Kind == UsValueKind.Text)
            {
                payload = body.Text;
                contentType = "text/plain; charset=utf-8";
            }
            else
            {
                payload = UsEmitJson.Write(body, start, length).Text;
                contentType = "application/json";
            }
        }

        var headers = receiver.Reference is UsHttpClientBuilder builder
            ? builder.Headers
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        UsEmitHost.Log.Write(UsLogLevel.Trace, "HTTP " + method + " " + target + " " + SecretRedactor.FormatHeadersForLog(headers));
        var response = await UsEmitHost.Transport.SendAsync(method, target, payload, contentType, headers).ConfigureAwait(false);
        return UsValue.FromReference(response);
    }

    public static UsValue Json(
        UsValue receiver,
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaultJsonOrEmpty,
        int start,
        int length) =>
        UsEmitAsync.Box(JsonAsync(receiver, typeName, fieldNames, typeNames, nullables, required, defaultJsonOrEmpty, start, length));

    public static async Task<UsValue> JsonAsync(
        UsValue receiver,
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaultJsonOrEmpty,
        int start,
        int length)
    {
        receiver = await UnwrapAsync(receiver).ConfigureAwait(false);
        if (receiver.Reference is not UsHttpClientResponse response)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "json yanıt ister.", start, length);
        }

        return UsEmitJson.Read(
            UsValue.FromText(response.Body),
            typeName,
            fieldNames,
            typeNames,
            nullables,
            required,
            defaultJsonOrEmpty,
            start,
            length);
    }

    private static async Task<UsValue> UnwrapAsync(UsValue value)
    {
        if (value.Reference is UsOperation operation)
        {
            return await operation.Task.ConfigureAwait(false);
        }

        return value;
    }

    public static UsValue Result(int statusCode, UsValue body) =>
        UsValue.FromReference(new UsHttpResult(statusCode, body));

    public static UsValue ResultNoContent() =>
        UsValue.FromReference(new UsHttpResult(204, UsValue.Null));

    public static async Task<UsValue> BoxUnitTask(Task task)
    {
        await task.ConfigureAwait(false);
        return UsValue.Null;
    }

    public static void Register(
        string method,
        string template,
        string[] parameterNames,
        string[] parameterKinds,
        string[] parameterTypeNames,
        string[] bodyShapes,
        string[] setupServices,
        Func<UsValue[], UsValue> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        AddRoute(method, template, parameterNames, parameterKinds, parameterTypeNames, bodyShapes, setupServices, async: false, sync: handler, asyncHandler: null);
    }

    public static void RegisterAsync(
        string method,
        string template,
        string[] parameterNames,
        string[] parameterKinds,
        string[] parameterTypeNames,
        string[] bodyShapes,
        string[] setupServices,
        Func<UsValue[], Task<UsValue>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        AddRoute(method, template, parameterNames, parameterKinds, parameterTypeNames, bodyShapes, setupServices, async: true, sync: null, asyncHandler: handler);
    }

    public static UsValue Dispatch(UsValue method, UsValue path, UsValue body) =>
        UsEmitAsync.Box(DispatchAsync(method.Text ?? string.Empty, path.Text ?? string.Empty, body.IsNull ? null : body.Text));

    public static Task<UsValue> DispatchAsync(string method, string path, string? body) =>
        DispatchAsync(new UsHttpRequest(method, path, body));

    /// <summary>Kestrel / host adapter entry: full request including query and content type.</summary>
    public static async Task<UsHttpResponse> DispatchRequestAsync(UsHttpRequest request)
    {
        var encoded = await DispatchAsync(request).ConfigureAwait(false);
        return encoded.Reference as UsHttpResponse
            ?? new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
    }

    public static async Task<UsValue> DispatchAsync(UsHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var method = request.Method;
        var path = request.Path;
        var match = Match(method, path);
        if (match is null)
        {
            var allowed = AllowedMethods(path);
            if (allowed.Count == 0)
            {
                return EncodeResponse(new UsHttpResponse(404, """{"hata":"bulunamadı"}""", "application/json"));
            }

            if (!allowed.Contains(method, StringComparer.Ordinal))
            {
                var response = new UsHttpResponse(405, """{"hata":"yöntem uygun değil"}""", "application/json");
                response.Headers["Allow"] = string.Join(", ", allowed);
                return EncodeResponse(response);
            }

            return EncodeResponse(new UsHttpResponse(404, """{"hata":"bulunamadı"}""", "application/json"));
        }

        UsEmitDi.PushScope();
        try
        {
            foreach (var service in match.Entry.SetupServices)
            {
                if (string.IsNullOrEmpty(service))
                {
                    continue;
                }

                _ = UsEmitDi.Resolve(service, 0, 0);
            }

            UsValue[] arguments;
            try
            {
                arguments = BindArguments(match, request);
            }
            catch (UsRuntimeException exception) when (IsBinding(exception.Code))
            {
                var status = exception.Code == UsRuntimeCodes.UnsupportedMediaType ? 415 : 400;
                return EncodeResponse(new UsHttpResponse(status, JsonError(exception.Message), "application/json"));
            }

            UsValue result;
            try
            {
                result = match.Entry.Async
                    ? await match.Entry.AsyncHandler!(arguments).ConfigureAwait(false)
                    : match.Entry.SyncHandler!(arguments);
            }
            catch (Exception exception)
            {
                var detail = "route handler: " + exception.GetType().Name + ": " + exception.Message;
                UsEmitHost.Log.Write(UsLogLevel.Error, detail);
                // stderr for host diagnostics — never included in the HTTP body.
                Console.Error.WriteLine(detail);
                return EncodeResponse(new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json"));
            }

            return EncodeResponse(ToResponse(result));
        }
        finally
        {
            UsEmitDi.PopScope();
        }
    }

    /// <summary>
    /// Test hook: when <c>US_TEST_ROUTE</c> is set to <c>"METHOD path"</c>, dispatch and write status/body to stdout.
    /// </summary>
    public static void MaybeDispatchTestRoute()
    {
        var route = Environment.GetEnvironmentVariable("US_TEST_ROUTE");
        if (string.IsNullOrWhiteSpace(route))
        {
            return;
        }

        var space = route.IndexOf(' ');
        if (space <= 0 || space >= route.Length - 1)
        {
            return;
        }

        var method = route[..space];
        var path = route[(space + 1)..];
        var body = Environment.GetEnvironmentVariable("US_TEST_ROUTE_BODY");
        var contentType = Environment.GetEnvironmentVariable("US_TEST_ROUTE_CONTENT_TYPE");
        var request = new UsHttpRequest(method, path, body, contentType);
        var encoded = DispatchAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        if (encoded.Reference is not UsHttpResponse response)
        {
            return;
        }

        Console.Out.WriteLine(response.StatusCode.ToString(CultureInfo.InvariantCulture));
        Console.Out.WriteLine(response.Body ?? string.Empty);
    }

    /// <summary>
    /// Production listen hook after test-route dispatch.
    /// Loads Us.Hosting when present; no-op when routes absent, US_TEST_ROUTE set, or hosting DLL missing.
    /// </summary>
    public static void MaybeListen()
    {
        EnsureHostingLoaded();
        ProductionListen?.Invoke();
    }

    private static void EnsureHostingLoaded()
    {
        if (ProductionListen is not null)
        {
            return;
        }

        try
        {
            var runtimeDir = Path.GetDirectoryName(typeof(UsEmitHttp).Assembly.Location);
            if (string.IsNullOrEmpty(runtimeDir))
            {
                return;
            }

            var hostingPath = Path.Combine(runtimeDir, "Us.Hosting.dll");
            if (File.Exists(hostingPath))
            {
                System.Reflection.Assembly.LoadFrom(hostingPath);
            }
        }
        catch (Exception)
        {
            // Hosting optional for non-web / in-process-only scenarios.
        }
    }

    public static void BootstrapTestHttpFromEnvironment()
    {
        var json = Environment.GetEnvironmentVariable("US_TEST_HTTP");
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var transport = new FakeHttpTransport();
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var key = property.Name;
            var space = key.IndexOf(' ');
            if (space <= 0 || space >= key.Length - 1)
            {
                continue;
            }

            var method = key[..space];
            var url = key[(space + 1)..];
            var status = property.Value.TryGetProperty("status", out var statusNode) && statusNode.TryGetInt32(out var code)
                ? code
                : 200;
            var body = property.Value.TryGetProperty("body", out var bodyNode)
                ? bodyNode.ValueKind == JsonValueKind.String ? bodyNode.GetString() ?? string.Empty : bodyNode.GetRawText()
                : string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (property.Value.TryGetProperty("headers", out var headersNode) && headersNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headersNode.EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString() ?? string.Empty;
                }
            }

            var contentType = headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
            transport.Register(method, url, new UsHttpClientResponse(status, body, contentType, headers));
        }

        UsEmitHost.Transport = transport;
    }

    public static FakeHttpTransport ParseTestHttpMap(string json)
    {
        var transport = new FakeHttpTransport();
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var key = property.Name;
            var space = key.IndexOf(' ');
            if (space <= 0 || space >= key.Length - 1)
            {
                continue;
            }

            var method = key[..space];
            var url = key[(space + 1)..];
            var status = property.Value.TryGetProperty("status", out var statusNode) && statusNode.TryGetInt32(out var code)
                ? code
                : 200;
            var body = property.Value.TryGetProperty("body", out var bodyNode)
                ? bodyNode.ValueKind == JsonValueKind.String ? bodyNode.GetString() ?? string.Empty : bodyNode.GetRawText()
                : string.Empty;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (property.Value.TryGetProperty("headers", out var headersNode) && headersNode.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in headersNode.EnumerateObject())
                {
                    headers[header.Name] = header.Value.GetString() ?? string.Empty;
                }
            }

            var contentType = headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
            transport.Register(method, url, new UsHttpClientResponse(status, body, contentType, headers));
        }

        return transport;
    }

    private static void AddRoute(
        string method,
        string template,
        string[] parameterNames,
        string[] parameterKinds,
        string[] parameterTypeNames,
        string[] bodyShapes,
        string[] setupServices,
        bool async,
        Func<UsValue[], UsValue>? sync,
        Func<UsValue[], Task<UsValue>>? asyncHandler)
    {
        var parts = (template ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new string[parts.Length];
        var isParameter = new bool[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (part.Length >= 2 && part[0] == '{' && part[^1] == '}')
            {
                segments[index] = part[1..^1];
                isParameter[index] = true;
            }
            else
            {
                segments[index] = part;
                isParameter[index] = false;
            }
        }

        var entry = new RouteEntry
        {
            Method = method,
            Template = template ?? "/",
            Segments = segments,
            IsParameter = isParameter,
            ParameterNames = parameterNames ?? [],
            ParameterKinds = parameterKinds ?? [],
            ParameterTypeNames = parameterTypeNames ?? [],
            BodyShapes = bodyShapes ?? [],
            SetupServices = setupServices ?? [],
            Async = async,
            SyncHandler = sync,
            AsyncHandler = asyncHandler,
            Specificity = isParameter.Count(flag => !flag) * 1000 + segments.Length,
        };

        lock (Sync)
        {
            Routes.Add(entry);
            Routes.Sort((left, right) =>
            {
                var bySpec = right.Specificity.CompareTo(left.Specificity);
                if (bySpec != 0)
                {
                    return bySpec;
                }

                var byTemplate = string.Compare(left.Template, right.Template, StringComparison.Ordinal);
                return byTemplate != 0
                    ? byTemplate
                    : string.Compare(left.Method, right.Method, StringComparison.Ordinal);
            });
        }
    }

    private sealed class MatchResult
    {
        public required RouteEntry Entry { get; init; }

        public required Dictionary<string, string> Values { get; init; }
    }

    private static MatchResult? Match(string method, string path)
    {
        path = NormalizePath(path);
        List<RouteEntry> snapshot;
        lock (Sync)
        {
            snapshot = [.. Routes];
        }

        foreach (var entry in snapshot)
        {
            if (!TryMatch(entry, path, out var values))
            {
                continue;
            }

            if (string.Equals(entry.Method, method, StringComparison.Ordinal))
            {
                return new MatchResult { Entry = entry, Values = values };
            }
        }

        return null;
    }

    private static IReadOnlyList<string> AllowedMethods(string path)
    {
        path = NormalizePath(path);
        var methods = new List<string>();
        List<RouteEntry> snapshot;
        lock (Sync)
        {
            snapshot = [.. Routes];
        }

        foreach (var entry in snapshot)
        {
            if (TryMatch(entry, path, out _) && !methods.Contains(entry.Method, StringComparer.Ordinal))
            {
                methods.Add(entry.Method);
            }
        }

        return methods;
    }

    private static bool TryMatch(RouteEntry entry, string path, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != entry.Segments.Length)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (entry.IsParameter[index])
            {
                values[entry.Segments[index]] = Uri.UnescapeDataString(segments[index]);
                continue;
            }

            if (!string.Equals(entry.Segments[index], segments[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var trimmed = path.Split('?', 2)[0];
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    private static UsValue[] BindArguments(MatchResult match, UsHttpRequest request)
    {
        var entry = match.Entry;
        var values = new UsValue[entry.ParameterNames.Length];
        for (var index = 0; index < entry.ParameterNames.Length; index++)
        {
            var name = entry.ParameterNames[index];
            var kind = entry.ParameterKinds[index];
            var typeName = entry.ParameterTypeNames[index];
            if (kind == "route" && match.Values.TryGetValue(name, out var routeValue))
            {
                values[index] = ConvertScalar(routeValue, typeName);
                continue;
            }

            if (kind == "body")
            {
                EnsureJsonContentType(request.ContentType);
                var shape = index < entry.BodyShapes.Length ? entry.BodyShapes[index] : typeName;
                values[index] = ReadBody(request.Body, shape);
                continue;
            }

            if (kind == "query" && request.Query.TryGetValue(name, out var queryValue))
            {
                values[index] = ConvertScalar(queryValue, typeName);
                continue;
            }

            if (request.Query.TryGetValue(name, out var looseQuery))
            {
                values[index] = ConvertScalar(looseQuery, typeName);
                continue;
            }

            if (typeName.EndsWith('?'))
            {
                values[index] = UsValue.Null;
                continue;
            }

            throw new UsRuntimeException(UsRuntimeCodes.BadRouteParameter, $"'{name}' parametresi eksik.", 0, 0);
        }

        return values;
    }

    private static void EnsureJsonContentType(string? contentType)
    {
        var text = contentType ?? string.Empty;
        if (text.Length > 0 && !text.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.UnsupportedMediaType,
                "İçerik tipi application/json olmalıdır.",
                0,
                0);
        }
    }

    private static UsValue ReadBody(string? body, string shape)
    {
        var text = body ?? string.Empty;
        if (shape.StartsWith('@') || shape.StartsWith('{'))
        {
            var decoded = UsEmitJson.DecodeShape(shape);
            return UsEmitJson.Read(
                UsValue.FromText(text),
                decoded.TypeName,
                decoded.FieldNames,
                decoded.TypeNames,
                decoded.Nullables,
                decoded.Required,
                decoded.Defaults,
                0,
                0);
        }

        return UsEmitJson.Read(UsValue.FromText(text), shape, [], [], [], [], [], 0, 0);
    }

    private static UsValue ConvertScalar(string text, string typeName)
    {
        var bare = typeName.TrimEnd('?');
        try
        {
            return bare switch
            {
                "metin" => UsValue.FromText(text),
                "karakter" => text.Length == 1 ? UsValue.FromChar(text[0]) : throw new FormatException(),
                "tamsayı" => UsValue.FromInt(int.Parse(text, CultureInfo.InvariantCulture)),
                "utamsayı" => UsValue.FromUInt(uint.Parse(text, CultureInfo.InvariantCulture)),
                "ondalık" => UsValue.FromFloat64(double.Parse(text, CultureInfo.InvariantCulture)),
                "gondalık" => UsValue.FromFloat32(float.Parse(text, CultureInfo.InvariantCulture)),
                "hondalık" => UsValue.FromDecimal(decimal.Parse(text, CultureInfo.InvariantCulture)),
                "mantıksal" => text switch
                {
                    "true" or "doğru" => UsValue.FromBool(true),
                    "false" or "yanlış" => UsValue.FromBool(false),
                    _ => throw new FormatException(),
                },
                _ => throw new FormatException(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.BadRouteParameter, $"'{text}' değerine dönüştürülemedi.", 0, 0);
        }
    }

    private static UsHttpResponse ToResponse(UsValue result)
    {
        if (result.Reference is UsHttpResult http)
        {
            if (http.StatusCode == 204)
            {
                return new UsHttpResponse(204);
            }

            if (http.Body.IsNull || http.Body.Kind == UsValueKind.Unit)
            {
                return new UsHttpResponse(http.StatusCode);
            }

            if (http.Body.Kind == UsValueKind.Text)
            {
                return new UsHttpResponse(http.StatusCode, http.Body.Text, "text/plain; charset=utf-8");
            }

            return new UsHttpResponse(http.StatusCode, UsEmitJson.Write(http.Body, 0, 0).Text, "application/json");
        }

        if (result.IsNull || result.Kind == UsValueKind.Unit)
        {
            return new UsHttpResponse(204);
        }

        if (result.Kind == UsValueKind.Text)
        {
            return new UsHttpResponse(200, result.Text, "text/plain; charset=utf-8");
        }

        try
        {
            return new UsHttpResponse(200, UsEmitJson.Write(result, 0, 0).Text, "application/json");
        }
        catch (UsRuntimeException)
        {
            return new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
        }
    }

    private static UsValue EncodeResponse(UsHttpResponse response) => UsValue.FromReference(response);

    private static bool IsBinding(string code) =>
        code is UsRuntimeCodes.BadRouteParameter or UsRuntimeCodes.UnsupportedMediaType
            or UsRuntimeCodes.UnknownJsonField or UsRuntimeCodes.MissingJsonField
            or UsRuntimeCodes.JsonMalformed or UsRuntimeCodes.JsonType or UsRuntimeCodes.JsonNull;

    private static string JsonError(string message)
    {
        var escaped = message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return "{\"hata\":\"" + escaped + "\"}";
    }
}
