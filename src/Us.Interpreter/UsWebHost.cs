using System.Globalization;
using System.Text;
using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class UsWebHost
{
    private readonly BoundCompilation _compilation;
    private readonly SourceFile _file;
    private readonly UsHost _host;
    private readonly RouteTable _routes;
    private readonly UsContainer _container;
    private readonly MemoryDatabase? _database;
    private readonly Evaluator _factory;

    public UsWebHost(BoundCompilation compilation, SourceFile file, UsHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(file);
        _compilation = compilation;
        _file = file;
        _host = host ?? new UsHost();
        _routes = new RouteTable(compilation.Routes);
        _database = compilation.Databases.IsDefaultOrEmpty ? null : new MemoryDatabase(compilation.Databases);
        _factory = new Evaluator(new ListOutput(), _host);
        _container = new UsContainer(compilation.Services, _factory.ConstructServicePublic);
        Routes = _routes;
        Database = _database;
    }

    public RouteTable Routes { get; }

    public MemoryDatabase? Database { get; }

    public UsContainer Container => _container;

    public async ValueTask<UsHttpResponse> DispatchAsync(UsHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var path = request.Path.Split('?', 2)[0];
        var query = ParseQuery(request.Path.Contains('?', StringComparison.Ordinal) ? request.Path.Split('?', 2)[1] : null);
        foreach (var pair in request.Query)
        {
            query[pair.Key] = pair.Value;
        }

        var allowed = _routes.AllowedMethods(path);
        if (allowed.Count == 0)
        {
            return new UsHttpResponse(404, """{"hata":"bulunamadı"}""", "application/json");
        }

        if (!allowed.Contains(request.Method, StringComparer.Ordinal))
        {
            var response = new UsHttpResponse(405, """{"hata":"yöntem uygun değil"}""", "application/json");
            response.Headers["Allow"] = string.Join(", ", allowed);
            return response;
        }

        var match = _routes.Match(request.Method, path);
        if (match is null || !string.Equals(match.Route.Method, request.Method, StringComparison.Ordinal))
        {
            return new UsHttpResponse(404, """{"hata":"bulunamadı"}""", "application/json");
        }

        var scope = _container.CreateScope();
        var output = new ListOutput();
        var evaluator = new Evaluator(output, _host);
        evaluator.AttachShared(_container, scope, _database);
        await evaluator.EnterPublicAsync().ConfigureAwait(false);
        try
        {
            foreach (var setup in match.Route.Controller.Setup)
            {
                await evaluator.ExecutePublic(setup).ConfigureAwait(false);
            }

            await evaluator.EnsureSetupAssignedAsync(match.Route.Controller).ConfigureAwait(false);

            UsValue[] arguments;
            try
            {
                arguments = await BindArguments(evaluator, match, request, query).ConfigureAwait(false);
            }
            catch (UsRuntimeException exception) when (IsBinding(exception.Code))
            {
                var status = exception.Code == UsRuntimeCodes.UnsupportedMediaType ? 415 : 400;
                return new UsHttpResponse(status, JsonError(exception.Message), "application/json");
            }

            UsValue result;
            try
            {
                result = await evaluator.InvokeHandlerAsync(match.Route.Handler, arguments, match.Route.Span).ConfigureAwait(false);
            }
            catch (UsThrown)
            {
                return new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
            }
            catch (UsRuntimeException)
            {
                return new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
            }
            catch (Exception)
            {
                return new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
            }

            return await ToResponse(evaluator, result).ConfigureAwait(false);
        }
        finally
        {
            evaluator.ExitPublic();
            scope.Close();
        }
    }

    private async ValueTask<UsValue[]> BindArguments(Evaluator evaluator, RouteMatch match, UsHttpRequest request, Dictionary<string, string> query)
    {
        var handler = match.Route.Handler;
        var values = new UsValue[handler.Parameters.Count];
        for (var index = 0; index < handler.Parameters.Count; index++)
        {
            var parameter = handler.Parameters[index];
            if (match.Values.TryGetValue(parameter.Name, out var routeValue))
            {
                values[index] = ConvertScalar(routeValue, parameter.Type, match.Route.Span);
                continue;
            }

            if (IsModel(parameter.Type))
            {
                values[index] = await BindBody(evaluator, request, parameter.Type, match.Route.Span).ConfigureAwait(false);
                continue;
            }

            if (query.TryGetValue(parameter.Name, out var queryValue))
            {
                values[index] = ConvertScalar(queryValue, parameter.Type, match.Route.Span);
                continue;
            }

            if (parameter.Type.IsNullable)
            {
                values[index] = UsValue.Null;
                continue;
            }

            throw new UsRuntimeException(UsRuntimeCodes.BadRouteParameter, $"'{parameter.Name}' parametresi eksik.", match.Route.Span.Start, match.Route.Span.Length);
        }

        return values;
    }

    private async ValueTask<UsValue> BindBody(Evaluator evaluator, UsHttpRequest request, TypeSymbol type, TextSpan span)
    {
        var contentType = request.ContentType ?? string.Empty;
        if (contentType.Length > 0 && !contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnsupportedMediaType, "İçerik tipi application/json olmalıdır.", span.Start, span.Length);
        }

        var body = request.Body ?? string.Empty;
        return await evaluator.ReadJsonTextPublic(body, type, span).ConfigureAwait(false);
    }

    private static UsValue ConvertScalar(string text, TypeSymbol type, TextSpan span)
    {
        var bare = type.WithNullable(false);
        if (bare is not PrimitiveTypeSymbol primitive)
        {
            throw new UsRuntimeException(UsRuntimeCodes.BadRouteParameter, "Rota parametresi dönüştürülemedi.", span.Start, span.Length);
        }

        try
        {
            return primitive.Canonical.Name switch
            {
                "metin" => UsValue.FromText(text),
                "karakter" => text.Length == 1
                    ? UsValue.FromChar(text[0])
                    : throw new FormatException(),
                "tamsayı" => UsValue.FromInt(int.Parse(text, CultureInfo.InvariantCulture)),
                "utamsayı" => UsValue.FromUInt(uint.Parse(text, CultureInfo.InvariantCulture)),
                "ondalık" => UsValue.FromDecimal(decimal.Parse(text, CultureInfo.InvariantCulture)),
                "gondalık" => UsValue.FromFloat32(float.Parse(text, CultureInfo.InvariantCulture)),
                "hondalık" => UsValue.FromFloat64(double.Parse(text, CultureInfo.InvariantCulture)),
                "mantıksal" => text switch
                {
                    "true" or "doğru" => UsValue.FromBool(true),
                    "false" or "yanlış" => UsValue.FromBool(false),
                    _ => throw new FormatException(),
                },
                "tarih" => UsValue.FromReference(new UsDate(DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
                "saat" => UsValue.FromReference(new UsTime(TimeOnly.ParseExact(text, "HH:mm:ss", CultureInfo.InvariantCulture))),
                "tarihsaat" => UsValue.FromReference(new UsDateTime(DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))),
                _ => throw new FormatException(),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.BadRouteParameter, $"'{text}' değerine dönüştürülemedi.", span.Start, span.Length);
        }
    }

    private async ValueTask<UsHttpResponse> ToResponse(Evaluator evaluator, UsValue result)
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

            return new UsHttpResponse(http.StatusCode, evaluator.WriteJsonPublic(http.Body, default), "application/json");
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
            return new UsHttpResponse(200, evaluator.WriteJsonPublic(result, default), "application/json");
        }
        catch (UsRuntimeException)
        {
            return new UsHttpResponse(500, """{"hata":"sunucu hatası"}""", "application/json");
        }
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static bool IsModel(TypeSymbol type)
    {
        var bare = type.WithNullable(false);
        return bare is SourceTypeSymbol source && source.Definition.TypeKind == SourceTypeKind.Model;
    }

    private static bool IsBinding(string code) =>
        code is UsRuntimeCodes.BadRouteParameter or UsRuntimeCodes.UnsupportedMediaType
            or UsRuntimeCodes.UnknownJsonField or UsRuntimeCodes.MissingJsonField
            or UsRuntimeCodes.JsonMalformed or UsRuntimeCodes.JsonType or UsRuntimeCodes.JsonNull;

    private static string JsonError(string message)
    {
        var escaped = message.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        return "{\"hata\":\"" + escaped + "\"}";
    }

    public static UsWebHost FromText(string path, string text, UsHost? host = null)
    {
        var file = SourceFile.FromText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var compiled = UsCompile.Front(file);
        if (compiled.Errors.Length > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, compiled.Errors.Select(item => item.Code + " " + item.Message)));
        }

        return new UsWebHost(compiled.Compilation, file, host);
    }
}
