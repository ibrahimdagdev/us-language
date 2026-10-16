namespace Us.Runtime;

/// <summary>
/// Provider-neutral structured query expression for table filters.
/// Values stay as <see cref="UsValue"/> — never SQL text fragments.
/// </summary>
public abstract class UsQueryExpr
{
    private UsQueryExpr()
    {
    }

    public static UsQueryExpr Field(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new FieldExpr(name);
    }

    public static UsQueryExpr Constant(UsValue value) => new ConstantExpr(value);

    public static UsQueryExpr Compare(UsQueryExpr left, string op, UsQueryExpr right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentException.ThrowIfNullOrEmpty(op);
        return new CompareExpr(left, op, right);
    }

    public static UsQueryExpr And(UsQueryExpr left, UsQueryExpr right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new AndExpr(left, right);
    }

    public static UsQueryExpr Or(UsQueryExpr left, UsQueryExpr right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new OrExpr(left, right);
    }

    public static UsQueryExpr Not(UsQueryExpr operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new NotExpr(operand);
    }

    /// <summary>Truthy test for a field (e.g. <c>^.aktif</c>).</summary>
    public static UsQueryExpr Truthy(UsQueryExpr operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        return new TruthyExpr(operand);
    }

    public sealed class FieldExpr : UsQueryExpr
    {
        internal FieldExpr(string name) => Name = name;

        public string Name { get; }
    }

    public sealed class ConstantExpr : UsQueryExpr
    {
        internal ConstantExpr(UsValue value) => Value = value;

        public UsValue Value { get; }
    }

    public sealed class CompareExpr : UsQueryExpr
    {
        internal CompareExpr(UsQueryExpr left, string op, UsQueryExpr right)
        {
            Left = left;
            Op = op;
            Right = right;
        }

        public UsQueryExpr Left { get; }

        public string Op { get; }

        public UsQueryExpr Right { get; }
    }

    public sealed class AndExpr : UsQueryExpr
    {
        internal AndExpr(UsQueryExpr left, UsQueryExpr right)
        {
            Left = left;
            Right = right;
        }

        public UsQueryExpr Left { get; }

        public UsQueryExpr Right { get; }
    }

    public sealed class OrExpr : UsQueryExpr
    {
        internal OrExpr(UsQueryExpr left, UsQueryExpr right)
        {
            Left = left;
            Right = right;
        }

        public UsQueryExpr Left { get; }

        public UsQueryExpr Right { get; }
    }

    public sealed class NotExpr : UsQueryExpr
    {
        internal NotExpr(UsQueryExpr operand) => Operand = operand;

        public UsQueryExpr Operand { get; }
    }

    public sealed class TruthyExpr : UsQueryExpr
    {
        internal TruthyExpr(UsQueryExpr operand) => Operand = operand;

        public UsQueryExpr Operand { get; }
    }
}

public sealed class UsQuerySort
{
    public UsQuerySort(string fieldName, bool descending)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        FieldName = fieldName;
        Descending = descending;
    }

    public string FieldName { get; }

    public bool Descending { get; }
}

/// <summary>Fully structured table query plan for memory or SQL providers.</summary>
public sealed class UsQueryPlan
{
    public UsQueryPlan(
        string tableName,
        IReadOnlyList<UsQueryExpr> filters,
        IReadOnlyList<UsQuerySort> sorts,
        IReadOnlyList<string> includes)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);
        TableName = tableName;
        Filters = filters;
        Sorts = sorts;
        Includes = includes;
    }

    public string TableName { get; }

    public IReadOnlyList<UsQueryExpr> Filters { get; }

    public IReadOnlyList<UsQuerySort> Sorts { get; }

    public IReadOnlyList<string> Includes { get; }
}

/// <summary>Schema metadata registered by emit bootstrap / provider.</summary>
public sealed class UsTableSchema
{
    public required string Name { get; init; }

    public required string ModelName { get; init; }

    public required string[] FieldNames { get; init; }

    public required string[] FieldTypes { get; init; }

    public required bool[] Nullable { get; init; }

    public required bool[] Required { get; init; }

    public string? KeyField { get; init; }

    public Dictionary<string, UsRelationSchema> Relations { get; } = new(StringComparer.Ordinal);
}

public sealed class UsRelationSchema
{
    public required string FieldName { get; init; }

    public required string RelatedTableName { get; init; }

    public required string RelatedKeyField { get; init; }
}
