namespace Us.Runtime;

/// <summary>
/// String-keyed DI container for emitted IL. Mirrors interpreter <c>UsContainer</c> /
/// <c>UsServiceScope</c> semantics without referencing Us.Semantics.
/// Request scopes use <see cref="AsyncLocal{T}"/> so concurrent Kestrel requests stay isolated.
/// </summary>
public static class UsEmitDi
{
    public const int LifetimeScoped = 0;
    public const int LifetimeSingleton = 1;
    public const int LifetimeTransient = 2;

    private sealed class Registration
    {
        public required string TypeName { get; init; }

        public required int Lifetime { get; init; }

        public required Func<UsValue> Factory { get; init; }
    }

    private sealed class Scope
    {
        public Dictionary<string, UsValue> Cache { get; } = new(StringComparer.Ordinal);

        public bool Closed { get; set; }
    }

    private static readonly Dictionary<string, Registration> Registrations = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, UsValue> Singletons = new(StringComparer.Ordinal);
    private static readonly object Sync = new();
    private static readonly AsyncLocal<Scope?> CurrentScope = new();
    private static readonly AsyncLocal<Stack<Scope?>?> ScopeStack = new();

    [ThreadStatic]
    private static List<string>? _chain;

    public static void Reset()
    {
        lock (Sync)
        {
            Registrations.Clear();
            Singletons.Clear();
        }

        CurrentScope.Value = null;
        ScopeStack.Value = null;
        _chain = null;
    }

    public static void Register(string typeName, int lifetime, Func<UsValue> factory)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(factory);
        lock (Sync)
        {
            if (!Registrations.TryAdd(typeName, new Registration
            {
                TypeName = typeName,
                Lifetime = lifetime,
                Factory = factory,
            }))
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.AmbiguousService,
                    $"'{typeName}' birden fazla kez kayıtlı.",
                    0,
                    0);
            }
        }
    }

    public static void BeginScope()
    {
        CurrentScope.Value = new Scope();
        ScopeStack.Value = new Stack<Scope?>();
    }

    public static void EndScope()
    {
        var scope = CurrentScope.Value;
        if (scope is not null)
        {
            scope.Closed = true;
            scope.Cache.Clear();
        }

        CurrentScope.Value = null;
        var stack = ScopeStack.Value;
        if (stack is not null)
        {
            stack.Clear();
        }

        ScopeStack.Value = null;
    }

    /// <summary>
    /// Nested request scope (web dispatch). Outer Main scope is restored by <see cref="PopScope"/>.
    /// </summary>
    public static void PushScope()
    {
        var stack = ScopeStack.Value ??= new Stack<Scope?>();
        stack.Push(CurrentScope.Value);
        CurrentScope.Value = new Scope();
    }

    public static void PopScope()
    {
        var current = CurrentScope.Value;
        if (current is not null)
        {
            current.Closed = true;
            current.Cache.Clear();
        }

        var stack = ScopeStack.Value;
        CurrentScope.Value = stack is { Count: > 0 } ? stack.Pop() : null;
    }

    public static UsValue Resolve(string typeName, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        _chain ??= [];
        return ResolveCore(typeName, start, length, _chain);
    }

    private static UsValue ResolveCore(string typeName, int start, int length, List<string> chain)
    {
        var scope = CurrentScope.Value;
        if (scope is null || scope.Closed)
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.ScopeClosed,
                "Kapatılan kapsamdan servis çözülemez.",
                start,
                length);
        }

        Registration registration;
        lock (Sync)
        {
            if (!Registrations.TryGetValue(typeName, out registration!))
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.ServiceMissing,
                    $"'{typeName}' kayıtlı değil.",
                    start,
                    length);
            }
        }

        if (chain.Any(item => string.Equals(item, typeName, StringComparison.Ordinal)))
        {
            var text = string.Join(" → ", chain.Append(typeName));
            throw new UsRuntimeException(UsRuntimeCodes.ServiceCycle, text, start, length);
        }

        chain.Add(typeName);
        try
        {
            return registration.Lifetime switch
            {
                LifetimeSingleton => ResolveCached(Singletons, Sync, registration, scope, chain, start, length),
                LifetimeScoped => ResolveCached(scope.Cache, Sync, registration, scope, chain, start, length),
                _ => registration.Factory(),
            };
        }
        finally
        {
            chain.RemoveAt(chain.Count - 1);
        }
    }

    private static UsValue ResolveCached(
        Dictionary<string, UsValue> cache,
        object sync,
        Registration registration,
        Scope scope,
        List<string> chain,
        int start,
        int length)
    {
        _ = chain;
        var key = registration.TypeName;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var existing))
            {
                return existing;
            }
        }

        // Keep chain visible to nested Resolve calls from the factory.
        var value = registration.Factory();
        lock (sync)
        {
            if (!scope.Closed || registration.Lifetime == LifetimeSingleton)
            {
                cache[key] = value;
            }

            if (scope.Closed && registration.Lifetime != LifetimeSingleton)
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.ScopeClosed,
                    "Kapatılan kapsamdan servis çözülemez.",
                    start,
                    length);
            }
        }

        return value;
    }
}
