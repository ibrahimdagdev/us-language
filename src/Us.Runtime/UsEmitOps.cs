namespace Us.Runtime;

/// <summary>
/// CLR helpers used by DotNetEmitBackend. Not an interpreter wrapper.
/// </summary>
public static class UsEmitOps
{
    public static string Display(UsValue value) => value.ToDisplay();

    public static bool RequireBool(UsValue value, int start, int length)
    {
        if (value.Kind != UsValueKind.Bool)
        {
            throw new UsRuntimeException(UsRuntimeCodes.RuntimeCondition, "Koşul mantıksal olmalıdır.", start, length);
        }

        return value.Bool;
    }

    public static int RequireInt(UsValue value, int start, int length)
    {
        if (value.Kind != UsValueKind.Int)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Aralık tamsayı ister.", start, length);
        }

        return value.Int;
    }

    public static UsValue RequireNumber(UsValue value, int start, int length)
    {
        if (value.Kind is not (UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Sayısal değer gerekir.", start, length);
        }

        return value;
    }

    public static UsValue Convert(UsValue value, UsValueKind target, int start, int length)
    {
        if (value.IsNull)
        {
            return value;
        }

        if (target is UsValueKind.Object or UsValueKind.Text or UsValueKind.Bool or UsValueKind.Char)
        {
            return value.Kind == target || target == UsValueKind.Object ? value : UsMath.Convert(value, target, start, length);
        }

        return UsMath.Convert(value, target, start, length);
    }

    public static UsValue Store(UsValue value, bool nullable, int start, int length)
    {
        value.EnsureCanStore(nullable, start, length);
        return value;
    }

    public static bool IsNull(UsValue value) => value.IsNull;

    public static UsValue Coalesce(UsValue left, UsValue right) => left.IsNull ? right : left;

    public static UsValue FromLiteralInt(int value) => UsValue.FromInt(value);

    public static UsValue FromLiteralUInt(uint value) => UsValue.FromUInt(value);

    public static UsValue FromLiteralFloat64(double value) => UsValue.FromFloat64(value);

    public static UsValue FromLiteralFloat32(float value) => UsValue.FromFloat32(value);

    public static UsValue FromLiteralDecimal(decimal value) => UsValue.FromDecimal(value);

    public static UsValue FromLiteralBool(bool value) => UsValue.FromBool(value);

    public static UsValue FromLiteralChar(char value) => UsValue.FromChar(value);

    public static UsValue FromLiteralText(string value) => UsValue.FromText(value);

    public static UsValue Null() => UsValue.Null;

    public static void WriteLine(UsValue value) => Console.WriteLine(value.ToDisplay());

    public static void Write(UsValue value) => Console.Write(value.ToDisplay());

    public static void Clear() => Console.Clear();

    public static UsValue ReadLine()
    {
        var line = Console.ReadLine();
        return line is null
            ? throw new UsRuntimeException(UsRuntimeCodes.InputEnded, "Okunacak satır kalmadı.", 0, 0)
            : UsValue.FromText(line);
    }

    public static UsValue Ask(UsValue prompt)
    {
        Console.Write(prompt.ToDisplay());
        return ReadLine();
    }

    public static string ConcatParts(UsValue[] parts)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.ToDisplay());
        }

        return builder.ToString();
    }

    public static UsValue Interpolation(UsValue[] parts) => UsValue.FromText(ConcatParts(parts));

    public static void Throw(UsValue error, int start, int length)
    {
        var message = error.Kind == UsValueKind.Object && error.Reference is UsEmitObject obj
            ? (obj._SafeRead("mesaj") ?? error.ToDisplay())
            : error.ToDisplay();
        throw new UsThrown(error, string.IsNullOrEmpty(message) ? "Hata" : message, start, length);
    }

    public static UsValue ErrorFromException(Exception exception)
    {
        if (exception is UsThrown thrown)
        {
            return thrown.Error;
        }

        var message = exception.Message;
        var obj = new UsEmitObject("Hata", ["mesaj"], [false]);
        UsEmitObject.WriteField(UsValue.FromReference(obj), "mesaj", UsValue.FromText(message), 0, 0);
        return UsValue.FromReference(obj);
    }

    public static bool CatchMatches(Exception exception, string? typeName, bool untyped)
    {
        if (untyped)
        {
            return exception is UsThrown or UsRuntimeException;
        }

        if (exception is not UsThrown thrown || thrown.Error.Reference is not UsEmitObject obj)
        {
            return false;
        }

        return string.Equals(obj.TypeName, typeName, StringComparison.Ordinal);
    }
}
