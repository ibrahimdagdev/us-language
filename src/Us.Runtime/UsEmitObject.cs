namespace Us.Runtime;

/// <summary>
/// Runtime object layout for emitted IL (models/classes). Not an interpreter wrapper.
/// </summary>
public sealed class UsEmitObject
{
    private readonly Dictionary<string, UsValue> _fields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UsEmitHandler>> _events = new(StringComparer.Ordinal);
    private readonly string[] _fieldOrder;

    public UsEmitObject(string typeName, string[] fieldNames, bool[] nullableFields)
    {
        TypeName = typeName;
        _fieldOrder = fieldNames.ToArray();
        for (var index = 0; index < fieldNames.Length; index++)
        {
            _fields[fieldNames[index]] = UsValue.Null;
            Nullable[fieldNames[index]] = index < nullableFields.Length && nullableFields[index];
        }
    }

    public string TypeName { get; }

    public IReadOnlyList<string> FieldOrder => _fieldOrder;

    public Dictionary<string, bool> Nullable { get; } = new(StringComparer.Ordinal);

    public bool IsAssigned(string fieldName) => _assigned.Contains(fieldName);

    public bool TryGetField(string fieldName, out UsValue value) => _fields.TryGetValue(fieldName, out value!);

    public UsEmitObject Clone()
    {
        var clone = new UsEmitObject(TypeName, _fieldOrder, _fieldOrder.Select(name => Nullable.TryGetValue(name, out var flag) && flag).ToArray());
        foreach (var pair in _fields)
        {
            clone._fields[pair.Key] = pair.Value;
            if (_assigned.Contains(pair.Key))
            {
                clone._assigned.Add(pair.Key);
            }
        }

        return clone;
    }

    internal string? _SafeRead(string fieldName) =>
        _fields.TryGetValue(fieldName, out var value) ? value.ToDisplay() : null;

    public List<UsEmitHandler> Handlers(string eventName)
    {
        if (!_events.TryGetValue(eventName, out var handlers))
        {
            handlers = [];
            _events[eventName] = handlers;
        }

        return handlers;
    }

    public static UsValue Create(string typeName, string[] fieldNames, bool[] nullableFields) =>
        UsValue.FromReference(new UsEmitObject(typeName, fieldNames, nullableFields));

    public static UsValue ReadField(UsValue receiver, string fieldName, int start, int length)
    {
        var instance = Require(receiver, start, length);
        if (!instance._fields.TryGetValue(fieldName, out var value))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{fieldName}' alanı yok.", start, length);
        }

        if (!instance._assigned.Contains(fieldName) && value.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnassignedRead, $"'{fieldName}' hücresi yok.", start, length);
        }

        return value;
    }

    public static void WriteField(UsValue receiver, string fieldName, UsValue value, int start, int length)
    {
        var instance = Require(receiver, start, length);
        if (!instance._fields.ContainsKey(fieldName))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{fieldName}' alanı yok.", start, length);
        }

        var nullable = instance.Nullable.TryGetValue(fieldName, out var flag) && flag;
        value.EnsureCanStore(nullable, start, length);
        instance._fields[fieldName] = value;
        instance._assigned.Add(fieldName);
    }

    /// <summary>Clears assignment so the field behaves as never written (include miss).</summary>
    public static void ClearAssignment(UsValue receiver, string fieldName)
    {
        var instance = Require(receiver, fieldName.Length == 0 ? 0 : 0, 0);
        if (!instance._fields.ContainsKey(fieldName))
        {
            return;
        }

        instance._fields[fieldName] = UsValue.Null;
        instance._assigned.Remove(fieldName);
    }

    public static UsValue Copy(UsValue receiver, string[] fieldNames, UsValue[] values, int start, int length)
    {
        var source = Require(receiver, start, length);
        var clone = new UsEmitObject(source.TypeName, source._fields.Keys.ToArray(), source._fields.Keys.Select(name => source.Nullable.TryGetValue(name, out var n) && n).ToArray());
        foreach (var pair in source._fields)
        {
            clone._fields[pair.Key] = pair.Value;
            if (source._assigned.Contains(pair.Key))
            {
                clone._assigned.Add(pair.Key);
            }
        }

        for (var index = 0; index < fieldNames.Length; index++)
        {
            WriteField(UsValue.FromReference(clone), fieldNames[index], values[index], start, length);
        }

        return UsValue.FromReference(clone);
    }

    public static bool EqualsModel(UsValue left, UsValue right) => Same(left, right);

    private static bool Same(UsValue left, UsValue right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            UsValueKind.Null => true,
            UsValueKind.Bool => left.Bool == right.Bool,
            UsValueKind.Char => left.Char == right.Char,
            UsValueKind.Int => left.Int == right.Int,
            UsValueKind.UInt => left.UInt == right.UInt,
            UsValueKind.Float64 => left.Float64.Equals(right.Float64),
            UsValueKind.Float32 => left.Float32.Equals(right.Float32),
            UsValueKind.Decimal => left.Decimal == right.Decimal,
            UsValueKind.Text => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            UsValueKind.Object when left.Reference is UsEmitObject a && right.Reference is UsEmitObject b =>
                string.Equals(a.TypeName, b.TypeName, StringComparison.Ordinal)
                && a._fields.Count == b._fields.Count
                && a._fields.All(pair => b._fields.TryGetValue(pair.Key, out var other) && Same(pair.Value, other)),
            UsValueKind.Object => ReferenceEquals(left.Reference, right.Reference),
            _ => false,
        };
    }

    private static UsEmitObject Require(UsValue receiver, int start, int length)
    {
        if (receiver.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Alıcı ~.", start, length);
        }

        return receiver.Reference as UsEmitObject
            ?? throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Nesne bekleniyordu.", start, length);
    }
}

public sealed class UsEmitList
{
    public UsEmitList(string kind)
    {
        Kind = kind;
    }

    public string Kind { get; }

    public List<UsValue> Items { get; } = [];

    public static UsValue Create(string kind, UsValue[] items)
    {
        var list = new UsEmitList(kind);
        list.Items.AddRange(items);
        return UsValue.FromReference(list);
    }

    public static UsValue Get(UsValue receiver, UsValue index, int start, int length)
    {
        var list = Require(receiver, start, length);
        var i = UsEmitOps.RequireInt(index, start, length);
        if (i < 0 || i >= list.Items.Count)
        {
            throw new UsRuntimeException(UsRuntimeCodes.IndexRange, "Dizin aralık dışında.", start, length);
        }

        return list.Items[i];
    }

    public static void Set(UsValue receiver, UsValue index, UsValue value, int start, int length)
    {
        var list = Require(receiver, start, length);
        var i = UsEmitOps.RequireInt(index, start, length);
        if (i < 0 || i >= list.Items.Count)
        {
            throw new UsRuntimeException(UsRuntimeCodes.IndexRange, "Dizin aralık dışında.", start, length);
        }

        list.Items[i] = value;
    }

    public static UsValue Add(UsValue receiver, UsValue value, int start, int length)
    {
        Require(receiver, start, length).Items.Add(value);
        return receiver;
    }

    public static int Count(UsValue receiver, int start, int length) => Require(receiver, start, length).Items.Count;

    public static UsValue Filter(UsValue receiver, Func<UsValue, bool> predicate, int start, int length)
    {
        var source = Require(receiver, start, length);
        var result = new UsEmitList(source.Kind);
        foreach (var item in source.Items)
        {
            if (predicate(item))
            {
                result.Items.Add(item);
            }
        }

        return UsValue.FromReference(result);
    }

    public static UsValue Select(UsValue receiver, Func<UsValue, UsValue> selector, int start, int length)
    {
        var source = Require(receiver, start, length);
        var result = new UsEmitList(source.Kind);
        foreach (var item in source.Items)
        {
            result.Items.Add(selector(item));
        }

        return UsValue.FromReference(result);
    }

    public static UsValue First(UsValue receiver, int start, int length)
    {
        var list = Require(receiver, start, length);
        if (list.Items.Count == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.EmptyFirst, "Koleksiyon boş.", start, length);
        }

        return list.Items[0];
    }

    private static UsEmitList Require(UsValue receiver, int start, int length)
    {
        if (receiver.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Alıcı ~.", start, length);
        }

        return receiver.Reference as UsEmitList
            ?? throw new UsRuntimeException(UsRuntimeCodes.QueryTarget, "Sorgu hedefi koleksiyon değil.", start, length);
    }
}
