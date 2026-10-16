namespace Us.Runtime;

public static class UsMath
{
    public static UsValue Convert(UsValue value, UsValueKind target, int start, int length)
    {
        if (value.Kind == target)
        {
            return value;
        }

        if (value.Kind == UsValueKind.Int && target == UsValueKind.Float64)
        {
            return UsValue.FromFloat64(value.Int);
        }

        if (value.Kind == UsValueKind.Int && target == UsValueKind.Float32)
        {
            return UsValue.FromFloat32(value.Int);
        }

        if (value.Kind == UsValueKind.Int && target == UsValueKind.Decimal)
        {
            return UsValue.FromDecimal(value.Int);
        }

        if (value.Kind == UsValueKind.UInt && target == UsValueKind.Float64)
        {
            return UsValue.FromFloat64(value.UInt);
        }

        throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Bu dönüşüm anlam tablosunda yok.", start, length);
    }

    public static UsValue Add(UsValue left, UsValue right, UsValueKind target, int start, int length) =>
        Arithmetic(left, right, target, start, length, (a, b) => a + b, (a, b) => a + b, (a, b) => a + b, (a, b) => a + b, (a, b) => a + b);

    public static UsValue Subtract(UsValue left, UsValue right, UsValueKind target, int start, int length) =>
        Arithmetic(left, right, target, start, length, (a, b) => a - b, (a, b) => a - b, (a, b) => a - b, (a, b) => a - b, (a, b) => a - b);

    public static UsValue Multiply(UsValue left, UsValue right, UsValueKind target, int start, int length) =>
        Arithmetic(left, right, target, start, length, (a, b) => a * b, (a, b) => a * b, (a, b) => a * b, (a, b) => a * b, (a, b) => a * b);

    public static UsValue Divide(UsValue left, UsValue right, UsValueKind target, int start, int length)
    {
        RequireDivisor(right, target, start, length);
        return Arithmetic(left, right, target, start, length, (a, b) => a / b, (a, b) => a / b, (a, b) => a / b, (a, b) => a / b, (a, b) => a / b);
    }

    public static UsValue Remainder(UsValue left, UsValue right, UsValueKind target, int start, int length)
    {
        RequireDivisor(right, target, start, length);
        return Arithmetic(left, right, target, start, length, (a, b) => a % b, (a, b) => a % b, (a, b) => a % b, (a, b) => a % b, (a, b) => a % b);
    }

    public static UsValue Negate(UsValue value, int start, int length) => value.Kind switch
    {
        UsValueKind.Int => UsValue.FromInt(unchecked(-value.Int)),
        UsValueKind.Float64 => UsValue.FromFloat64(-value.Float64),
        UsValueKind.Float32 => UsValue.FromFloat32(-value.Float32),
        UsValueKind.Decimal => UsValue.FromDecimal(-value.Decimal),
        _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Bu değer olumsuzlanamaz.", start, length),
    };

    public static bool Compare(UsValue left, UsValue right, string op, int start, int length)
    {
        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            var order = string.CompareOrdinal(left.Text, right.Text);
            return op switch
            {
                "==" => order == 0,
                "!=" => order != 0,
                "<" => order < 0,
                "<=" => order <= 0,
                ">" => order > 0,
                ">=" => order >= 0,
                _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Karşılaştırma işleci yok.", start, length),
            };
        }

        if (left.Kind == UsValueKind.Bool && right.Kind == UsValueKind.Bool)
        {
            return op switch
            {
                "==" => left.Bool == right.Bool,
                "!=" => left.Bool != right.Bool,
                _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Mantıksal değer sıralanamaz.", start, length),
            };
        }

        if (left.Kind == UsValueKind.Char && right.Kind == UsValueKind.Char)
        {
            return op switch
            {
                "==" => left.Char == right.Char,
                "!=" => left.Char != right.Char,
                _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Karakter sıralanamaz.", start, length),
            };
        }

        var target = CommonKind(left, right, start, length);
        var a = Convert(left, target, start, length);
        var b = Convert(right, target, start, length);
        var compared = CompareNumbers(a, b);
        return op switch
        {
            "==" => compared == 0,
            "!=" => compared != 0,
            "<" => compared < 0,
            "<=" => compared <= 0,
            ">" => compared > 0,
            ">=" => compared >= 0,
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Karşılaştırma işleci yok.", start, length),
        };
    }

    private static UsValue Arithmetic(
        UsValue left,
        UsValue right,
        UsValueKind target,
        int start,
        int length,
        Func<int, int, int> ints,
        Func<uint, uint, uint> uints,
        Func<double, double, double> floats,
        Func<float, float, float> singles,
        Func<decimal, decimal, decimal> decimals)
    {
        var a = Convert(left, target, start, length);
        var b = Convert(right, target, start, length);
        return target switch
        {
            UsValueKind.Int => UsValue.FromInt(ints(a.Int, b.Int)),
            UsValueKind.UInt => UsValue.FromUInt(uints(a.UInt, b.UInt)),
            UsValueKind.Float64 => UsValue.FromFloat64(floats(a.Float64, b.Float64)),
            UsValueKind.Float32 => UsValue.FromFloat32(singles(a.Float32, b.Float32)),
            UsValueKind.Decimal => UsValue.FromDecimal(decimals(a.Decimal, b.Decimal)),
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Sayısal sonuç tipi yok.", start, length),
        };
    }

    private static void RequireDivisor(UsValue right, UsValueKind target, int start, int length)
    {
        var divisor = Convert(right, target, start, length);
        var zero = target switch
        {
            UsValueKind.Int => divisor.Int == 0,
            UsValueKind.UInt => divisor.UInt == 0,
            UsValueKind.Float64 => divisor.Float64 == 0,
            UsValueKind.Float32 => divisor.Float32 == 0,
            UsValueKind.Decimal => divisor.Decimal == 0,
            _ => true,
        };
        if (zero)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DivisionByZero, "Sıfıra bölünemez.", start, length);
        }
    }

    private static UsValueKind CommonKind(UsValue left, UsValue right, int start, int length)
    {
        if (left.Kind == right.Kind && IsNumber(left.Kind))
        {
            return left.Kind;
        }

        if (CanConvert(left.Kind, right.Kind))
        {
            return right.Kind;
        }

        if (CanConvert(right.Kind, left.Kind))
        {
            return left.Kind;
        }

        throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Sayılar ortak tipe gelmiyor.", start, length);
    }

    private static bool CanConvert(UsValueKind from, UsValueKind to) =>
        from == to
        || from == UsValueKind.Int && to is UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal
        || from == UsValueKind.UInt && to == UsValueKind.Float64;

    private static bool IsNumber(UsValueKind kind) =>
        kind is UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal;

    private static int CompareNumbers(UsValue left, UsValue right) => left.Kind switch
    {
        UsValueKind.Int => left.Int.CompareTo(right.Int),
        UsValueKind.UInt => left.UInt.CompareTo(right.UInt),
        UsValueKind.Float64 => left.Float64.CompareTo(right.Float64),
        UsValueKind.Float32 => left.Float32.CompareTo(right.Float32),
        UsValueKind.Decimal => left.Decimal.CompareTo(right.Decimal),
        _ => 1,
    };
}
