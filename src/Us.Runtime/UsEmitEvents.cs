namespace Us.Runtime;

/// <summary>
/// US event handler entry for compiled programs. CLR events are not used —
/// subscription list + snapshot semantics match the interpreter (M8).
/// </summary>
public sealed class UsEmitHandler
{
    public UsEmitHandler(string slotId, UsValue receiver, Action<UsValue, UsValue> invoke, bool isVirtual)
    {
        SlotId = slotId ?? throw new ArgumentNullException(nameof(slotId));
        Receiver = receiver;
        Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        IsVirtual = isVirtual;
    }

    public string SlotId { get; }

    public UsValue Receiver { get; }

    public Action<UsValue, UsValue> Invoke { get; }

    public bool IsVirtual { get; }

    public bool Same(UsEmitHandler other) =>
        string.Equals(SlotId, other.SlotId, StringComparison.Ordinal)
        && ReferenceEquals(Receiver.Reference, other.Receiver.Reference);
}

/// <summary>
/// Virtual/override dispatch table for compiled instance methods.
/// </summary>
public static class UsEmitVTable
{
    private static readonly Dictionary<string, string?> Bases = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<string, Action<UsValue, UsValue>>> Tables = new(StringComparer.Ordinal);

    public static void SetBase(string typeName, string? baseTypeName) =>
        Bases[typeName] = string.IsNullOrEmpty(baseTypeName) ? null : baseTypeName;

    public static void Register(string typeName, string slotId, Action<UsValue, UsValue> invoke)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(slotId);
        ArgumentNullException.ThrowIfNull(invoke);
        if (!Tables.TryGetValue(typeName, out var table))
        {
            table = new Dictionary<string, Action<UsValue, UsValue>>(StringComparer.Ordinal);
            Tables[typeName] = table;
        }

        table[slotId] = invoke;
    }

    public static void InvokeVirtual(UsValue receiver, string slotId, UsValue argument, int start, int length)
    {
        var typeName = RequireObject(receiver, start, length).TypeName;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = typeName; current is not null && seen.Add(current); current = Bases.GetValueOrDefault(current))
        {
            if (Tables.TryGetValue(current, out var table) && table.TryGetValue(slotId, out var invoke))
            {
                invoke(receiver, argument);
                return;
            }
        }

        throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"'{slotId}' metodu yok.", start, length);
    }

    private static UsEmitObject RequireObject(UsValue receiver, int start, int length)
    {
        if (receiver.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Boş örneğin üyesi çağrılamaz.", start, length);
        }

        return receiver.Reference as UsEmitObject
            ?? throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Nesne bekleniyordu.", start, length);
    }
}

/// <summary>
/// Shared event semantics for DotNetEmitBackend (subscribe / unsubscribe / trigger).
/// </summary>
public static class UsEmitEvents
{
    public static void Add(
        UsValue owner,
        string eventName,
        string slotId,
        UsValue receiver,
        Action<UsValue, UsValue> invoke,
        bool isVirtual,
        int start,
        int length)
    {
        RequireObject(owner, start, length).Handlers(eventName).Add(new UsEmitHandler(slotId, receiver, invoke, isVirtual));
    }

    public static void Remove(
        UsValue owner,
        string eventName,
        string slotId,
        UsValue receiver,
        Action<UsValue, UsValue> invoke,
        bool isVirtual,
        int start,
        int length)
    {
        var handlers = RequireObject(owner, start, length).Handlers(eventName);
        var probe = new UsEmitHandler(slotId, receiver, invoke, isVirtual);
        var index = handlers.FindIndex(item => item.Same(probe));
        if (index < 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingSubscription, "Çıkarılacak dinleyici yok.", start, length);
        }

        handlers.RemoveAt(index);
    }

    public static void Trigger(UsValue owner, string eventName, UsValue argument, int start, int length)
    {
        var snapshot = RequireObject(owner, start, length).Handlers(eventName).ToArray();
        var errors = new List<Exception>();
        foreach (var handler in snapshot)
        {
            try
            {
                if (handler.IsVirtual)
                {
                    UsEmitVTable.InvokeVirtual(handler.Receiver, handler.SlotId, argument, start, length);
                }
                else
                {
                    handler.Invoke(handler.Receiver, argument);
                }
            }
            catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count == 1)
        {
            throw errors[0];
        }

        if (errors.Count > 1)
        {
            throw new UsAggregateException(errors, start, length);
        }
    }

    public static void RejectAsyncHandler(int start, int length) =>
        throw new UsRuntimeException(UsRuntimeCodes.AsyncHandler, "Olay işleyicisi eşzamansız olamaz.", start, length);

    private static UsEmitObject RequireObject(UsValue owner, int start, int length)
    {
        if (owner.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Boş örneğin üyesi çağrılamaz.", start, length);
        }

        return owner.Reference as UsEmitObject
            ?? throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Nesne bekleniyordu.", start, length);
    }
}
