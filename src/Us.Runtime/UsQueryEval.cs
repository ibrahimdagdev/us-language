namespace Us.Runtime;

/// <summary>Evaluates <see cref="UsQueryExpr"/> against an in-memory row (memory provider).</summary>
public static class UsQueryEval
{
    public static bool Matches(UsQueryExpr expr, UsEmitObject row) =>
        IsTruthy(Evaluate(expr, row));

    public static UsValue Evaluate(UsQueryExpr expr, UsEmitObject row) => expr switch
    {
        UsQueryExpr.FieldExpr field => ReadField(row, field.Name),
        UsQueryExpr.ConstantExpr constant => constant.Value,
        UsQueryExpr.CompareExpr compare => UsValue.FromBool(Compare(Evaluate(compare.Left, row), compare.Op, Evaluate(compare.Right, row))),
        UsQueryExpr.AndExpr and => UsValue.FromBool(IsTruthy(Evaluate(and.Left, row)) && IsTruthy(Evaluate(and.Right, row))),
        UsQueryExpr.OrExpr or => UsValue.FromBool(IsTruthy(Evaluate(or.Left, row)) || IsTruthy(Evaluate(or.Right, row))),
        UsQueryExpr.NotExpr not => UsValue.FromBool(!IsTruthy(Evaluate(not.Operand, row))),
        UsQueryExpr.TruthyExpr truthy => UsValue.FromBool(IsTruthy(Evaluate(truthy.Operand, row))),
        _ => UsValue.Null,
    };

    private static UsValue ReadField(UsEmitObject row, string name)
    {
        if (!row.IsAssigned(name) || !row.TryGetField(name, out var value))
        {
            return UsValue.Null;
        }

        return value;
    }

    private static bool IsTruthy(UsValue value) => value.Kind switch
    {
        UsValueKind.Bool => value.Bool,
        UsValueKind.Null => false,
        UsValueKind.Int => value.Int != 0,
        UsValueKind.UInt => value.UInt != 0,
        UsValueKind.Text => !string.IsNullOrEmpty(value.Text),
        _ => !value.IsNull,
    };

    private static bool Compare(UsValue left, string op, UsValue right)
    {
        if (op is "==" or "!=")
        {
            var equal = Same(left, right);
            return op == "==" ? equal : !equal;
        }

        if (left.IsNull || right.IsNull)
        {
            return false;
        }

        var order = CompareOrder(left, right);
        return op switch
        {
            ">" => order > 0,
            ">=" => order >= 0,
            "<" => order < 0,
            "<=" => order <= 0,
            _ => false,
        };
    }

    private static bool Same(UsValue left, UsValue right)
    {
        if (left.IsNull && right.IsNull)
        {
            return true;
        }

        if (left.IsNull || right.IsNull)
        {
            return false;
        }

        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            return string.Equals(left.Text, right.Text, StringComparison.Ordinal);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return ToDecimal(left) == ToDecimal(right);
        }

        if (left.Kind == UsValueKind.Bool && right.Kind == UsValueKind.Bool)
        {
            return left.Bool == right.Bool;
        }

        return left.Equals(right);
    }

    private static int CompareOrder(UsValue left, UsValue right)
    {
        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            return string.CompareOrdinal(left.Text, right.Text);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return ToDecimal(left).CompareTo(ToDecimal(right));
        }

        return string.CompareOrdinal(left.ToDisplay(), right.ToDisplay());
    }

    private static bool IsNumber(UsValue value) =>
        value.Kind is UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal;

    private static decimal ToDecimal(UsValue value) => value.Kind switch
    {
        UsValueKind.Int => value.Int,
        UsValueKind.UInt => value.UInt,
        UsValueKind.Decimal => value.Decimal,
        UsValueKind.Float32 => (decimal)value.Float32,
        UsValueKind.Float64 => (decimal)value.Float64,
        _ => 0,
    };
}
