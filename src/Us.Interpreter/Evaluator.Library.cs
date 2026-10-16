using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed partial class Evaluator
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private async ValueTask<UsValue> RunIntrinsic(FunctionSymbol function, BoundNode node)
    {
        var arguments = await Arguments(function, node).ConfigureAwait(false);
        var receiver = node.Children.Length > function.Parameters.Count
            ? await Evaluate(node.Children[0]).ConfigureAwait(false)
            : UsValue.Unit;
        switch (function.Intrinsic)
        {
            case Intrinsic.WriteLine:
                _output.WriteLine(arguments[0].ToDisplay());
                return UsValue.Unit;
            case Intrinsic.Write:
                _output.Write(arguments[0].ToDisplay());
                return UsValue.Unit;
            case Intrinsic.Clear:
                _output.Clear();
                return UsValue.Unit;
            case Intrinsic.Ask:
                _output.Write(arguments[0].ToDisplay());
                return UsValue.FromText(ReadInput(node.Span));
            case Intrinsic.ReadLine:
                return UsValue.FromText(ReadInput(node.Span));
            case Intrinsic.TextTrim:
                return UsValue.FromText((receiver.Text ?? string.Empty).Trim());
            case Intrinsic.TextUpper:
                return UsValue.FromText((receiver.Text ?? string.Empty).ToUpper(Turkish));
            case Intrinsic.TextLower:
                return UsValue.FromText((receiver.Text ?? string.Empty).ToLower(Turkish));
            case Intrinsic.TextContains:
                return UsValue.FromBool((receiver.Text ?? string.Empty).Contains(arguments[0].Text ?? string.Empty, StringComparison.Ordinal));
            case Intrinsic.TextStarts:
                return UsValue.FromBool((receiver.Text ?? string.Empty).StartsWith(arguments[0].Text ?? string.Empty, StringComparison.Ordinal));
            case Intrinsic.TextEnds:
                return UsValue.FromBool((receiver.Text ?? string.Empty).EndsWith(arguments[0].Text ?? string.Empty, StringComparison.Ordinal));
            case Intrinsic.TextReplace:
                return UsValue.FromText((receiver.Text ?? string.Empty).Replace(arguments[0].Text ?? string.Empty, arguments[1].Text ?? string.Empty, StringComparison.Ordinal));
            case Intrinsic.TextSplit:
                return Split(receiver.Text ?? string.Empty, arguments[0].Text ?? string.Empty, node);
            case Intrinsic.TextJoin:
                return UsValue.FromText(string.Join(receiver.Text ?? string.Empty, RequireCollection(arguments[0], node.Span).Items.Select(item => item.ToDisplay())));
            case Intrinsic.JsonWrite:
                return UsValue.FromText(WriteJson(arguments[0], node.Span));
            case Intrinsic.JsonRead:
                return await ReadJsonText(arguments[0].Text ?? string.Empty, node.Type, node.Span).ConfigureAwait(false);
            case Intrinsic.FileRead:
                return UsValue.FromText(File(node.Span, () => _host.Files.Read(arguments[0].Text ?? string.Empty)));
            case Intrinsic.FileWrite:
                File(node.Span, () => _host.Files.Write(arguments[0].Text ?? string.Empty, arguments[1].Text ?? string.Empty));
                return UsValue.Unit;
            case Intrinsic.FileAppend:
                File(node.Span, () => _host.Files.Append(arguments[0].Text ?? string.Empty, arguments[1].Text ?? string.Empty));
                return UsValue.Unit;
            case Intrinsic.FileDelete:
                File(node.Span, () => _host.Files.Delete(arguments[0].Text ?? string.Empty));
                return UsValue.Unit;
            case Intrinsic.FileExists:
                return UsValue.FromBool(File(node.Span, () => _host.Files.Exists(arguments[0].Text ?? string.Empty)));
            case Intrinsic.DirectoryCreate:
                File(node.Span, () => _host.Files.CreateDirectory(arguments[0].Text ?? string.Empty));
                return UsValue.Unit;
            case Intrinsic.DirectoryDelete:
                File(node.Span, () => _host.Files.DeleteDirectory(arguments[0].Text ?? string.Empty));
                return UsValue.Unit;
            case Intrinsic.DirectoryExists:
                return UsValue.FromBool(File(node.Span, () => _host.Files.DirectoryExists(arguments[0].Text ?? string.Empty)));
            case Intrinsic.DirectoryFiles:
                return Files(File(node.Span, () => _host.Files.ListFiles(arguments[0].Text ?? string.Empty)), node.Type);
            case Intrinsic.PathCombine:
                return UsValue.FromText(File(node.Span, () => _host.Files.Combine(arguments[0].Text ?? string.Empty, arguments[1].Text ?? string.Empty)));
            case Intrinsic.LogTrace:
                _host.Log.Write(UsLogLevel.Trace, FormatLogMessage(function, node, arguments[0]));
                return UsValue.Unit;
            case Intrinsic.LogInfo:
                _host.Log.Write(UsLogLevel.Information, FormatLogMessage(function, node, arguments[0]));
                return UsValue.Unit;
            case Intrinsic.LogWarning:
                _host.Log.Write(UsLogLevel.Warning, FormatLogMessage(function, node, arguments[0]));
                return UsValue.Unit;
            case Intrinsic.LogError:
                _host.Log.Write(UsLogLevel.Error, FormatLogMessage(function, node, arguments[0]));
                return UsValue.Unit;
            case Intrinsic.LogCritical:
                _host.Log.Write(UsLogLevel.Critical, FormatLogMessage(function, node, arguments[0]));
                return UsValue.Unit;
            case Intrinsic.HttpOk:
                return UsValue.FromReference(new UsHttpResult(200, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpCreated:
                return UsValue.FromReference(new UsHttpResult(201, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpNoContent:
                return UsValue.FromReference(new UsHttpResult(204, UsValue.Null));
            case Intrinsic.HttpBadRequest:
                return UsValue.FromReference(new UsHttpResult(400, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpUnauthorized:
                return UsValue.FromReference(new UsHttpResult(401, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpForbidden:
                return UsValue.FromReference(new UsHttpResult(403, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpNotFound:
                return UsValue.FromReference(new UsHttpResult(404, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpConflict:
                return UsValue.FromReference(new UsHttpResult(409, arguments.Length > 0 ? arguments[0] : UsValue.Null));
            case Intrinsic.HttpHeader:
                return HttpHeader(receiver, arguments[0], arguments[1], node.Span);
            case Intrinsic.HttpGet:
                return await HttpSend("GET", receiver, arguments, node).ConfigureAwait(false);
            case Intrinsic.HttpPost:
                return await HttpSend("POST", receiver, arguments, node).ConfigureAwait(false);
            case Intrinsic.HttpPut:
                return await HttpSend("PUT", receiver, arguments, node).ConfigureAwait(false);
            case Intrinsic.HttpPatch:
                return await HttpSend("PATCH", receiver, arguments, node).ConfigureAwait(false);
            case Intrinsic.HttpDelete:
                return await HttpSend("DELETE", receiver, arguments, node).ConfigureAwait(false);
            case Intrinsic.HttpJson:
                return await HttpJson(receiver, node).ConfigureAwait(false);
            default:
                throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Yerleşik işlev yok.", node.Span.Start, node.Span.Length);
        }
    }

    private UsValue HttpHeader(UsValue receiver, UsValue name, UsValue value, TextSpan span)
    {
        var header = name.Text ?? string.Empty;
        if (header.Length == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.EmptyHeaderName, "Başlık adı boş olamaz.", span.Start, span.Length);
        }

        var builder = receiver.Reference as UsHttpClientBuilder ?? new UsHttpClientBuilder();
        if (receiver.Reference is UsHttpClientBuilder existing)
        {
            builder = existing;
        }

        builder.Headers[header] = value.Text ?? string.Empty;
        return UsValue.FromReference(builder);
    }

    private async ValueTask<UsValue> HttpSend(string method, UsValue receiver, UsValue[] arguments, BoundNode node)
    {
        var url = arguments[0].Text ?? string.Empty;
        string? body = null;
        string? contentType = null;
        if (arguments.Length > 1 && !arguments[1].IsNull && arguments[1].Kind != UsValueKind.Unit)
        {
            if (arguments[1].Kind == UsValueKind.Text)
            {
                body = arguments[1].Text;
                contentType = "text/plain; charset=utf-8";
            }
            else
            {
                body = WriteJson(arguments[1], node.Span);
                contentType = "application/json";
            }
        }

        var headers = receiver.Reference is UsHttpClientBuilder builder
            ? builder.Headers
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _host.Log.Write(UsLogLevel.Trace, "HTTP " + method + " " + url + " " + SecretRedactor.FormatHeadersForLog(headers));
        var response = await _host.Transport.SendAsync(method, url, body, contentType, headers).ConfigureAwait(false);
        return UsValue.FromReference(response);
    }

    private static string FormatLogMessage(FunctionSymbol function, BoundNode node, UsValue value)
    {
        var offset = node.Children.Length > function.Parameters.Count ? 1 : 0;
        if (offset < node.Children.Length && node.Children[offset].Symbol is FieldSymbol field)
        {
            return SecretRedactor.RedactNamed(field.Name, value.ToDisplay());
        }

        return value.ToDisplay();
    }

    private async ValueTask<UsValue> HttpJson(UsValue receiver, BoundNode node)
    {
        if (receiver.Reference is not UsHttpClientResponse response)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "json yanıt ister.", node.Span.Start, node.Span.Length);
        }

        return await ReadJsonText(response.Body, node.Type, node.Span).ConfigureAwait(false);
    }

    private string ReadInput(TextSpan span) =>
        _host.Input.ReadLine() ?? throw new UsRuntimeException(UsRuntimeCodes.InputEnded, "Okunacak satır kalmadı.", span.Start, span.Length);

    private static T File<T>(TextSpan span, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (UsRuntimeException exception) when (exception.Start == 0 && exception.Length == 0)
        {
            throw new UsRuntimeException(exception.Code, exception.Message, span.Start, span.Length);
        }
    }

    private static void File(TextSpan span, Action action) => File(span, () =>
    {
        action();
        return 0;
    });

    private static UsValue Files(IReadOnlyList<string> names, TypeSymbol type)
    {
        var element = type is ConstructedTypeSymbol constructed && constructed.Arguments.Length == 1 ? constructed.Arguments[0] : type;
        var list = new UsCollection("liste", element);
        foreach (var name in names)
        {
            list.Items.Add(UsValue.FromText(name));
        }

        return UsValue.FromReference(list);
    }

    private UsValue Split(string text, string separator, BoundNode node)
    {
        if (separator.Length == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Ayraç boş olamaz.", node.Span.Start, node.Span.Length);
        }

        var element = node.Type is ConstructedTypeSymbol constructed && constructed.Arguments.Length == 1 ? constructed.Arguments[0] : node.Type;
        var list = new UsCollection("liste", element);
        foreach (var part in text.Split(separator))
        {
            list.Items.Add(UsValue.FromText(part));
        }

        return UsValue.FromReference(list);
    }

    private async ValueTask<UsValue[]> Arguments(FunctionSymbol function, BoundNode node)
    {
        var values = new UsValue[function.Parameters.Count];
        var offset = node.Children.Length > function.Parameters.Count ? 1 : 0;
        if (offset == 0 && node.Children.Length > 0 && IsHttpIntrinsic(function.Intrinsic) &&
            (node.Children[0].Symbol is ModuleSymbol || node.Children[0].Type is HttpBuilderTypeSymbol or HttpResponseTypeSymbol))
        {
            offset = 1;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var index = offset + i;
            if (index >= node.Children.Length)
            {
                values[i] = function.Parameters[i].Type.IsNullable || function.Parameters[i].HasDefault ? UsValue.Null : UsValue.Unit;
                continue;
            }

            values[i] = await Evaluate(node.Children[index]).ConfigureAwait(false);
        }

        return values;
    }

    private static bool IsHttpIntrinsic(Intrinsic intrinsic) =>
        intrinsic is Intrinsic.HttpGet or Intrinsic.HttpPost or Intrinsic.HttpPut or Intrinsic.HttpPatch
            or Intrinsic.HttpDelete or Intrinsic.HttpHeader or Intrinsic.HttpJson;

    private static bool TryTemporal(UsValue left, UsValue right, TokenKind? op, TextSpan span, out UsValue result)
    {
        result = UsValue.Unit;
        if (left.Reference is UsDateTime instant && right.Reference is UsDuration duration && op is TokenKind.Plus or TokenKind.Minus)
        {
            try
            {
                var ticks = Ticks(duration.Milliseconds, span);
                if (op == TokenKind.Minus)
                {
                    ticks = checked(-ticks);
                }

                result = UsValue.FromReference(new UsDateTime(instant.Value.AddTicks(ticks)));
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
            {
                throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", span.Start, span.Length);
            }

            return true;
        }

        if (left.Reference is UsDateTime start && right.Reference is UsDateTime end && op == TokenKind.Minus)
        {
            result = UsValue.FromReference(new UsDuration((start.Value.Ticks - end.Value.Ticks) / TimeSpan.TicksPerMillisecond));
            return true;
        }

        if (left.Reference is UsDuration first && right.Reference is UsDuration second && op is TokenKind.Plus or TokenKind.Minus)
        {
            try
            {
                var milliseconds = op == TokenKind.Plus
                    ? checked(first.Milliseconds + second.Milliseconds)
                    : checked(first.Milliseconds - second.Milliseconds);
                result = UsValue.FromReference(new UsDuration(milliseconds));
            }
            catch (OverflowException)
            {
                throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", span.Start, span.Length);
            }

            return true;
        }

        if (!Order(left, right, out var order) || op is not (TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual))
        {
            return false;
        }

        result = UsValue.FromBool(op switch
        {
            TokenKind.EqualEqual => order == 0,
            TokenKind.BangEqual => order != 0,
            TokenKind.Less => order < 0,
            TokenKind.LessEqual => order <= 0,
            TokenKind.Greater => order > 0,
            _ => order >= 0,
        });
        return true;
    }

    private static long Ticks(long milliseconds, TextSpan span)
    {
        try
        {
            return checked(milliseconds * TimeSpan.TicksPerMillisecond);
        }
        catch (OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", span.Start, span.Length);
        }
    }

    private static bool Order(UsValue left, UsValue right, out int order)
    {
        order = (left.Reference, right.Reference) switch
        {
            (UsDate leftDate, UsDate rightDate) => leftDate.Value.CompareTo(rightDate.Value),
            (UsTime leftTime, UsTime rightTime) => leftTime.Value.CompareTo(rightTime.Value),
            (UsDateTime leftInstant, UsDateTime rightInstant) => leftInstant.Value.CompareTo(rightInstant.Value),
            (UsDuration leftDuration, UsDuration rightDuration) => leftDuration.Milliseconds.CompareTo(rightDuration.Milliseconds),
            _ => int.MinValue,
        };
        return order != int.MinValue || left.Reference is UsDate or UsTime or UsDateTime or UsDuration && right.Reference?.GetType() == left.Reference?.GetType();
    }

    private async ValueTask<UsValue> Validate(BoundNode node)
    {
        var target = await Evaluate(node.Children[0]).ConfigureAwait(false);
        var element = node.Type is ConstructedTypeSymbol constructed && constructed.Arguments.Length == 1 ? constructed.Arguments[0] : node.Type;
        var messages = new UsCollection("liste", element);
        if (target.Reference is UsInstance instance)
        {
            foreach (var field in instance.Definition.Fields)
            {
                var value = instance.Assigned(field) ? instance.Read(field, node.Span.Start, node.Span.Length) : UsValue.Null;
                if (field.IsRequired && IsEmpty(value))
                {
                    messages.Items.Add(UsValue.FromText($"'{field.Name}' zorunludur."));
                }

                if (field.MaxLength is not null && value.Kind == UsValueKind.Text)
                {
                    var limit = await Evaluate(field.MaxLength).ConfigureAwait(false);
                    if ((value.Text ?? string.Empty).Length > limit.Int)
                    {
                        messages.Items.Add(UsValue.FromText($"'{field.Name}' en çok {limit.Int} karakter olabilir."));
                    }
                }

                if (field.RangeMinimum is not null && field.RangeMaximum is not null && !value.IsNull && value.Kind != UsValueKind.Text)
                {
                    var minimum = await Evaluate(field.RangeMinimum).ConfigureAwait(false);
                    var maximum = await Evaluate(field.RangeMaximum).ConfigureAwait(false);
                    if (Number(value) < Number(minimum) || Number(value) > Number(maximum))
                    {
                        messages.Items.Add(UsValue.FromText($"'{field.Name}' aralık dışındadır."));
                    }
                }
            }
        }

        for (var i = 1; i + 1 < node.Children.Length; i += 2)
        {
            if (!await IsTrue(node.Children[i]).ConfigureAwait(false))
            {
                var message = await Evaluate(node.Children[i + 1]).ConfigureAwait(false);
                messages.Items.Add(UsValue.FromText(message.Kind == UsValueKind.Text ? message.Text ?? string.Empty : message.ToDisplay()));
            }
        }

        return UsValue.FromReference(messages);
    }

    private static bool IsEmpty(UsValue value) => value.IsNull || value.Kind == UsValueKind.Text && (value.Text ?? string.Empty).Length == 0;

    private static decimal Number(UsValue value) => value.Kind switch
    {
        UsValueKind.Int => value.Int,
        UsValueKind.UInt => value.UInt,
        UsValueKind.Float64 => (decimal)value.Float64,
        UsValueKind.Float32 => (decimal)value.Float32,
        UsValueKind.Decimal => value.Decimal,
        _ => 0,
    };

    private async ValueTask BindSetting(BoundNode node)
    {
        if (node.Symbol is not SourceTypeSymbol type || type.ConfigSection is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingSection, "Ayar bölümü yok.", node.Span.Start, node.Span.Length);
        }

        var text = _host.Configuration.ReadText();
        if (text is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingConfig, "uygulamaAyar.json bulunamadı.", node.Span.Start, node.Span.Length);
        }

        var document = ParseJson(text, node.Span);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(type.ConfigSection, out var section))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingSection, $"'{type.ConfigSection}' bölümü yok.", node.Span.Start, node.Span.Length);
        }

        var value = await ReadJson(section, type, node.Span).ConfigureAwait(false);
        _host.Services.Add(value.Reference!);
    }

    private string WriteJson(UsValue value, TextSpan span)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteJson(writer, value, [], span);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteJson(Utf8JsonWriter writer, UsValue value, HashSet<UsInstance> seen, TextSpan span)
    {
        if (value.IsNull || value.Kind == UsValueKind.Unit)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Kind)
        {
            case UsValueKind.Bool:
                writer.WriteBooleanValue(value.Bool);
                return;
            case UsValueKind.Int:
                writer.WriteNumberValue(value.Int);
                return;
            case UsValueKind.UInt:
                writer.WriteNumberValue(value.UInt);
                return;
            case UsValueKind.Float64:
                writer.WriteNumberValue(value.Float64);
                return;
            case UsValueKind.Float32:
                writer.WriteNumberValue(value.Float32);
                return;
            case UsValueKind.Decimal:
                writer.WriteNumberValue(value.Decimal);
                return;
            case UsValueKind.Text:
            case UsValueKind.Char:
                writer.WriteStringValue(value.ToDisplay());
                return;
        }

        if (value.Reference is UsCollection collection)
        {
            writer.WriteStartArray();
            foreach (var item in collection.Items)
            {
                WriteJson(writer, item, seen, span);
            }

            writer.WriteEndArray();
            return;
        }

        if (value.Reference is UsInstance instance)
        {
            if (!seen.Add(instance))
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonCycle, "JSON döngüsel nesne yazamaz.", span.Start, span.Length);
            }

            writer.WriteStartObject();
            foreach (var field in UsInstance.Fields(instance.Definition))
            {
                writer.WritePropertyName(field.Name);
                if (!instance.Assigned(field))
                {
                    if (field.Type.IsNullable)
                    {
                        writer.WriteNullValue();
                        continue;
                    }

                    throw new UsRuntimeException(UsRuntimeCodes.MissingJsonField, $"'{field.Name}' alanı yok.", span.Start, span.Length);
                }

                WriteJson(writer, instance.Read(field, span.Start, span.Length), seen, span);
            }

            writer.WriteEndObject();
            seen.Remove(instance);
            return;
        }

        writer.WriteStringValue(value.ToDisplay());
    }

    private async ValueTask<UsValue> ReadJsonText(string text, TypeSymbol type, TextSpan span)
    {
        var document = ParseJson(text, span);
        return await ReadJson(document.RootElement, type, span).ConfigureAwait(false);
    }

    private static JsonDocument ParseJson(string text, TextSpan span)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonMalformed, "JSON okunamadı.", span.Start, span.Length);
        }
    }

    private async ValueTask<UsValue> ReadJson(JsonElement element, TypeSymbol type, TextSpan span)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!type.IsNullable)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonNull, "null yalnız nullable alana yazılır.", span.Start, span.Length);
            }

            return UsValue.Null;
        }

        var model = type is SourceTypeSymbol source ? source.Definition : null;
        if (model?.TypeKind == SourceTypeKind.Model)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON nesne olmalıdır.", span.Start, span.Length);
            }

            var fields = UsInstance.Fields(model).ToArray();
            var names = new HashSet<string>(fields.Select(field => field.Name), StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Contains(property.Name))
                {
                    throw new UsRuntimeException(UsRuntimeCodes.UnknownJsonField, $"'{property.Name}' alanı modelde yok.", span.Start, span.Length);
                }
            }

            var instance = UsInstance.Allocate(type);
            await ApplyDefaults(instance, model, span).ConfigureAwait(false);
            foreach (var field in fields)
            {
                if (!element.TryGetProperty(field.Name, out var property))
                {
                    continue;
                }

                var value = await ReadJson(property, field.Type, span).ConfigureAwait(false);
                WriteField(UsValue.FromReference(instance), field, value, span);
            }

            foreach (var field in fields)
            {
                if (instance.Assigned(field))
                {
                    continue;
                }

                if (field.Type.IsNullable)
                {
                    instance.Write(field, UsValue.Null, type, span.Start, span.Length);
                    continue;
                }

                throw new UsRuntimeException(UsRuntimeCodes.MissingJsonField, $"'{field.Name}' alanı yok.", span.Start, span.Length);
            }

            return UsValue.FromReference(instance);
        }

        if (type is ConstructedTypeSymbol list && list.Definition is GenericDefinitionSymbol { Name: "liste" } && list.Arguments.Length == 1)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON dizi olmalıdır.", span.Start, span.Length);
            }

            var collection = new UsCollection("liste", list.Arguments[0]);
            foreach (var item in element.EnumerateArray())
            {
                collection.Items.Add(await ReadJson(item, list.Arguments[0], span).ConfigureAwait(false));
            }

            return UsValue.FromReference(collection);
        }

        var bare = type.WithNullable(false);
        if (bare is PrimitiveTypeSymbol primitive)
        {
            return ReadPrimitive(element, primitive.Canonical.Name, span);
        }

        throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Bu tip JSON ile okunamaz.", span.Start, span.Length);
    }

    private static UsValue ReadPrimitive(JsonElement element, string name, TextSpan span)
    {
        if (name == "metin")
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON metin olmalıdır.", span.Start, span.Length);
            }

            return UsValue.FromText(element.GetString() ?? string.Empty);
        }

        if (name == "mantıksal")
        {
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON mantıksal olmalıdır.", span.Start, span.Length);
            }

            return UsValue.FromBool(element.GetBoolean());
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON sayı olmalıdır.", span.Start, span.Length);
        }

        var raw = element.GetRawText();
        try
        {
            return name switch
            {
                "tamsayı" => UsValue.FromInt(int.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                "utamsayı" => UsValue.FromUInt(uint.Parse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                "ondalık" => UsValue.FromFloat64(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "gondalık" => UsValue.FromFloat32(float.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "hondalık" => UsValue.FromDecimal(decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture)),
                _ => throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Bu tip JSON sayısı olamaz.", span.Start, span.Length),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON sayısı hedef tipe uymuyor.", span.Start, span.Length);
        }
    }
}
