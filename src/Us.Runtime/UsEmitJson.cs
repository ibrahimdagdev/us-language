using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Us.Runtime;

/// <summary>
/// Symbol-driven JSON for emitted IL (<see cref="UsEmitObject"/>), matching Evaluator WriteJson/ReadJson field order and codes.
/// </summary>
public static class UsEmitJson
{
    public static UsValue Write(UsValue value, int start, int length)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteValue(writer, value, [], start, length);
        }

        return UsValue.FromText(Encoding.UTF8.GetString(stream.ToArray()));
    }

    public static UsValue Read(
        UsValue text,
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaultJsonOrEmpty,
        int start,
        int length)
    {
        var document = Parse(text.Text ?? string.Empty, start, length);
        using (document)
        {
            return ReadElement(
                document.RootElement,
                typeName,
                fieldNames,
                typeNames,
                nullables,
                required,
                defaultJsonOrEmpty,
                start,
                length);
        }
    }

    internal static UsValue ReadElement(
        JsonElement element,
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaultJsonOrEmpty,
        int start,
        int length)
    {
        var rootNullable = StripNullableMarker(ref typeName);
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!rootNullable)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonNull, "null yalnız nullable alana yazılır.", start, length);
            }

            return UsValue.Null;
        }

        if (string.Equals(typeName, "liste", StringComparison.Ordinal))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON dizi olmalıdır.", start, length);
            }

            var elementType = typeNames.Length > 0 ? typeNames[0] : "metin";
            var list = new UsEmitList("liste");
            foreach (var item in element.EnumerateArray())
            {
                list.Items.Add(ReadTyped(item, elementType, start, length));
            }

            return UsValue.FromReference(list);
        }

        if (IsPrimitiveName(typeName))
        {
            return ReadPrimitive(element, typeName, start, length);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON nesne olmalıdır.", start, length);
        }

        fieldNames ??= [];
        typeNames ??= [];
        nullables ??= [];
        required ??= [];
        defaultJsonOrEmpty ??= [];

        var names = new HashSet<string>(fieldNames, StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name))
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownJsonField, $"'{property.Name}' alanı modelde yok.", start, length);
            }
        }

        var nullableFlags = new bool[fieldNames.Length];
        for (var i = 0; i < fieldNames.Length; i++)
        {
            nullableFlags[i] = i < nullables.Length && nullables[i];
        }

        var instance = new UsEmitObject(typeName, fieldNames, nullableFlags);
        var boxed = UsValue.FromReference(instance);

        for (var i = 0; i < fieldNames.Length; i++)
        {
            var defaultJson = i < defaultJsonOrEmpty.Length ? defaultJsonOrEmpty[i] : string.Empty;
            if (defaultJson.Length == 0)
            {
                continue;
            }

            var defaultValue = ReadDefaultJson(defaultJson, typeNames[i], start, length);
            UsEmitObject.WriteField(boxed, fieldNames[i], defaultValue, start, length);
        }

        for (var i = 0; i < fieldNames.Length; i++)
        {
            if (!element.TryGetProperty(fieldNames[i], out var property))
            {
                continue;
            }

            var fieldNullable = i < nullables.Length && nullables[i];
            // JSON null into a field must stay US1541 (not UsValue.EnsureCanStore → US1500).
            if (property.ValueKind == JsonValueKind.Null)
            {
                if (!fieldNullable)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.JsonNull, "null yalnız nullable alana yazılır.", start, length);
                }

                UsEmitObject.WriteField(boxed, fieldNames[i], UsValue.Null, start, length);
                continue;
            }

            var fieldType = i < typeNames.Length ? typeNames[i] : "metin";
            var value = ReadTyped(property, fieldType, start, length);
            UsEmitObject.WriteField(boxed, fieldNames[i], value, start, length);
        }

        for (var i = 0; i < fieldNames.Length; i++)
        {
            if (instance.IsAssigned(fieldNames[i]))
            {
                continue;
            }

            var nullable = i < nullables.Length && nullables[i];
            if (nullable)
            {
                UsEmitObject.WriteField(boxed, fieldNames[i], UsValue.Null, start, length);
                continue;
            }

            throw new UsRuntimeException(UsRuntimeCodes.MissingJsonField, $"'{fieldNames[i]}' alanı yok.", start, length);
        }

        _ = required;
        return boxed;
    }

    private static void WriteValue(Utf8JsonWriter writer, UsValue value, HashSet<object> seen, int start, int length)
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

        if (value.Reference is UsEmitList list)
        {
            writer.WriteStartArray();
            foreach (var item in list.Items)
            {
                WriteValue(writer, item, seen, start, length);
            }

            writer.WriteEndArray();
            return;
        }

        if (value.Reference is UsEmitObject instance)
        {
            if (!seen.Add(instance))
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonCycle, "JSON döngüsel nesne yazamaz.", start, length);
            }

            writer.WriteStartObject();
            foreach (var field in instance.FieldOrder)
            {
                writer.WritePropertyName(field);
                if (!instance.IsAssigned(field))
                {
                    if (instance.Nullable.TryGetValue(field, out var nullable) && nullable)
                    {
                        writer.WriteNullValue();
                        continue;
                    }

                    throw new UsRuntimeException(UsRuntimeCodes.MissingJsonField, $"'{field}' alanı yok.", start, length);
                }

                instance.TryGetField(field, out var fieldValue);
                WriteValue(writer, fieldValue, seen, start, length);
            }

            writer.WriteEndObject();
            seen.Remove(instance);
            return;
        }

        writer.WriteStringValue(value.ToDisplay());
    }

    private static UsValue ReadTyped(JsonElement element, string typeDesc, int start, int length)
    {
        var nullable = StripNullableMarker(ref typeDesc);
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (!nullable)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonNull, "null yalnız nullable alana yazılır.", start, length);
            }

            return UsValue.Null;
        }

        if (typeDesc.StartsWith("@{", StringComparison.Ordinal))
        {
            var shape = DecodeShape(typeDesc, start, length);
            // Nested shape roots are non-nullable here; null already handled above via '?' marker.
            return ReadElement(
                element,
                shape.TypeName,
                shape.FieldNames,
                shape.TypeNames,
                shape.Nullables,
                shape.Required,
                shape.Defaults,
                start,
                length);
        }

        if (typeDesc.StartsWith("liste:", StringComparison.Ordinal))
        {
            var elementType = typeDesc["liste:".Length..];
            return ReadElement(element, "liste", [], [elementType], [], [], [], start, length);
        }

        if (IsPrimitiveName(typeDesc))
        {
            return ReadPrimitive(element, typeDesc, start, length);
        }

        // Bare model name without embedded shape cannot be reconstructed.
        throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Bu tip JSON ile okunamaz.", start, length);
    }

    /// <summary>Strips a trailing '?' nullable marker encoded by the emitter's DescribeType.</summary>
    private static bool StripNullableMarker(ref string typeDesc)
    {
        if (typeDesc.Length > 0 && typeDesc[^1] == '?')
        {
            typeDesc = typeDesc[..^1];
            return true;
        }

        return false;
    }

    private static UsValue ReadDefaultJson(string json, string typeDesc, int start, int length)
    {
        var document = Parse(json, start, length);
        using (document)
        {
            return ReadTyped(document.RootElement, typeDesc, start, length);
        }
    }

    private static JsonDocument Parse(string text, int start, int length)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonMalformed, "JSON okunamadı.", start, length);
        }
    }

    private static UsValue ReadPrimitive(JsonElement element, string name, int start, int length)
    {
        if (name == "metin")
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON metin olmalıdır.", start, length);
            }

            return UsValue.FromText(element.GetString() ?? string.Empty);
        }

        if (name == "mantıksal")
        {
            if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON mantıksal olmalıdır.", start, length);
            }

            return UsValue.FromBool(element.GetBoolean());
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON sayı olmalıdır.", start, length);
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
                _ => throw new UsRuntimeException(UsRuntimeCodes.JsonType, "Bu tip JSON sayısı olamaz.", start, length),
            };
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonType, "JSON sayısı hedef tipe uymuyor.", start, length);
        }
    }

    private static bool IsPrimitiveName(string name) =>
        name is "metin" or "mantıksal" or "tamsayı" or "utamsayı" or "ondalık" or "gondalık" or "hondalık" or "karakter";

    public static string EncodeShape(
        string typeName,
        string[] fieldNames,
        string[] typeNames,
        bool[] nullables,
        bool[] required,
        string[] defaults)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("n", typeName);
            writer.WritePropertyName("f");
            writer.WriteStartArray();
            foreach (var name in fieldNames)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("t");
            writer.WriteStartArray();
            foreach (var type in typeNames)
            {
                writer.WriteStringValue(type);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("z");
            writer.WriteStartArray();
            foreach (var flag in nullables)
            {
                writer.WriteBooleanValue(flag);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("r");
            writer.WriteStartArray();
            foreach (var flag in required)
            {
                writer.WriteBooleanValue(flag);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("d");
            writer.WriteStartArray();
            foreach (var item in defaults)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return "@" + Encoding.UTF8.GetString(stream.ToArray());
    }

    public static (string TypeName, string[] FieldNames, string[] TypeNames, bool[] Nullables, bool[] Required, string[] Defaults) DecodeShape(string encoded) =>
        DecodeShape(encoded, 0, 0) switch
        {
            var shape => (shape.TypeName, shape.FieldNames, shape.TypeNames, shape.Nullables, shape.Required, shape.Defaults),
        };

    private static Shape DecodeShape(string encoded, int start, int length)
    {
        var json = encoded.StartsWith('@') ? encoded[1..] : encoded;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.JsonMalformed, "JSON okunamadı.", start, length);
        }

        using (document)
        {
            var root = document.RootElement;
            return new Shape(
                root.GetProperty("n").GetString() ?? string.Empty,
                ReadStringArray(root.GetProperty("f")),
                ReadStringArray(root.GetProperty("t")),
                ReadBoolArray(root.GetProperty("z")),
                ReadBoolArray(root.GetProperty("r")),
                ReadStringArray(root.GetProperty("d")));
        }
    }

    private static string[] ReadStringArray(JsonElement element)
    {
        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(item.GetString() ?? string.Empty);
        }

        return list.ToArray();
    }

    private static bool[] ReadBoolArray(JsonElement element)
    {
        var list = new List<bool>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(item.GetBoolean());
        }

        return list.ToArray();
    }

    private readonly record struct Shape(
        string TypeName,
        string[] FieldNames,
        string[] TypeNames,
        bool[] Nullables,
        bool[] Required,
        string[] Defaults);
}
