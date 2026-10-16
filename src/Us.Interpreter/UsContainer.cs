using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class UsContainer
{
    private readonly Dictionary<SourceTypeSymbol, ServiceRegistration> _registrations = new();
    private readonly Dictionary<SourceTypeSymbol, UsValue> _singletons = new();
    private readonly Dictionary<SourceTypeSymbol, Task<UsValue>> _singletonFlight = new();
    private readonly object _sync = new();
    private readonly Func<ServiceRegistration, UsServiceScope, List<SourceTypeSymbol>, ValueTask<UsValue>> _factory;
    private int _resolves;

    public UsContainer(IEnumerable<ServiceRegistration> registrations, Func<ServiceRegistration, UsServiceScope, List<SourceTypeSymbol>, ValueTask<UsValue>> factory)
    {
        _factory = factory;
        foreach (var registration in registrations)
        {
            if (!_registrations.TryAdd(registration.ServiceType.Definition, registration))
            {
                throw new UsRuntimeException(UsRuntimeCodes.AmbiguousService, $"'{registration.ServiceType.Name}' birden fazla kez kayıtlı.", registration.Span.Start, registration.Span.Length);
            }
        }
    }

    public int Resolves => _resolves;

    public UsServiceScope CreateScope() => new(this);

    internal async ValueTask<UsValue> ResolveAsync(UsServiceScope scope, SourceTypeSymbol type, TextSpan span, List<SourceTypeSymbol> chain)
    {
        Interlocked.Increment(ref _resolves);
        if (scope.Closed)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ScopeClosed, "Kapatılan kapsamdan servis çözülemez.", span.Start, span.Length);
        }

        var definition = type.Definition;
        if (!_registrations.TryGetValue(definition, out var registration))
        {
            throw new UsRuntimeException(UsRuntimeCodes.ServiceMissing, $"'{definition.Name}' kayıtlı değil.", span.Start, span.Length);
        }

        if (chain.Any(item => ReferenceEquals(item, definition)))
        {
            var text = string.Join(" → ", chain.Select(item => item.Name).Append(definition.Name));
            throw new UsRuntimeException(UsRuntimeCodes.ServiceCycle, text, span.Start, span.Length);
        }

        chain.Add(definition);
        try
        {
            return registration.Lifetime switch
            {
                ServiceLifetime.Singleton => await SingleFlight(_singletons, _singletonFlight, _sync, registration, scope, chain).ConfigureAwait(false),
                ServiceLifetime.Scoped => await scope.GetScoped(registration, chain).ConfigureAwait(false),
                _ => await _factory(registration, scope, chain).ConfigureAwait(false),
            };
        }
        finally
        {
            chain.RemoveAt(chain.Count - 1);
        }
    }

    internal async ValueTask<UsValue> SingleFlight(Dictionary<SourceTypeSymbol, UsValue> cache, Dictionary<SourceTypeSymbol, Task<UsValue>> flight, object sync, ServiceRegistration registration, UsServiceScope scope, List<SourceTypeSymbol> chain)
    {
        var key = registration.ServiceType.Definition;
        TaskCompletionSource<UsValue>? created = null;
        Task<UsValue>? pending = null;
        UsValue? cached = null;
        lock (sync)
        {
            if (cache.TryGetValue(key, out var existing))
            {
                cached = existing;
            }
            else if (flight.TryGetValue(key, out var task))
            {
                pending = task;
            }
            else
            {
                created = new TaskCompletionSource<UsValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                flight[key] = created.Task;
            }
        }

        if (cached is UsValue hit)
        {
            return hit;
        }

        if (pending is not null)
        {
            return await pending.ConfigureAwait(false);
        }

        try
        {
            var value = await _factory(registration, scope, chain).ConfigureAwait(false);
            lock (sync)
            {
                if (!scope.Closed || registration.Lifetime == ServiceLifetime.Singleton)
                {
                    cache[key] = value;
                }

                flight.Remove(key);
            }

            if (scope.Closed && registration.Lifetime != ServiceLifetime.Singleton)
            {
                var closed = new UsRuntimeException(UsRuntimeCodes.ScopeClosed, "Kapatılan kapsamdan servis çözülemez.", registration.Span.Start, registration.Span.Length);
                created!.TrySetException(closed);
                _ = created.Task.Exception;
                throw closed;
            }

            created!.SetResult(value);
            return value;
        }
        catch (Exception exception)
        {
            lock (sync)
            {
                flight.Remove(key);
            }

            created!.TrySetException(exception);
            _ = created.Task.Exception;
            throw;
        }
    }
}

public sealed class UsServiceScope : IDisposable
{
    private readonly UsContainer _container;
    private readonly Dictionary<SourceTypeSymbol, UsValue> _cache = new();
    private readonly Dictionary<SourceTypeSymbol, Task<UsValue>> _flight = new();
    private readonly object _sync = new();
    private readonly List<UsInstance> _owned = [];

    internal UsServiceScope(UsContainer container) => _container = container;

    public bool Closed { get; private set; }

    public ValueTask<UsValue> ResolveAsync(SourceTypeSymbol type, TextSpan span = default) =>
        _container.ResolveAsync(this, type.Definition, span, []);

    internal ValueTask<UsValue> ResolveAsync(SourceTypeSymbol type, TextSpan span, List<SourceTypeSymbol> chain) =>
        _container.ResolveAsync(this, type.Definition, span, chain);

    internal ValueTask<UsValue> GetScoped(ServiceRegistration registration, List<SourceTypeSymbol> chain) =>
        _container.SingleFlight(_cache, _flight, _sync, registration, this, chain);

    internal void Own(UsInstance instance) => _owned.Add(instance);

    public void Close()
    {
        if (Closed)
        {
            return;
        }

        Closed = true;
        foreach (var instance in _owned)
        {
            instance.Released = true;
        }

        _owned.Clear();
        lock (_sync)
        {
            _cache.Clear();
            _flight.Clear();
        }
    }

    public void Dispose() => Close();
}
