using System.Collections.Immutable;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class RouteMatch
{
    public RouteMatch(RouteSymbol route, IReadOnlyDictionary<string, string> values)
    {
        Route = route;
        Values = values;
    }

    public RouteSymbol Route { get; }

    public IReadOnlyDictionary<string, string> Values { get; }
}

public sealed class RouteTable
{
    private readonly ImmutableArray<RouteEntry> _entries;

    public RouteTable(IEnumerable<RouteSymbol> routes)
    {
        _entries = routes
            .Select(route => new RouteEntry(route, Parse(route.Template)))
            .OrderByDescending(entry => entry.Specificity)
            .ThenBy(entry => entry.Route.Template, StringComparer.Ordinal)
            .ThenBy(entry => entry.Route.Method, StringComparer.Ordinal)
            .ToImmutableArray();
        Routes = routes.ToImmutableArray();
    }

    public ImmutableArray<RouteSymbol> Routes { get; }

    public RouteMatch? Match(string method, string path)
    {
        path = NormalizePath(path);
        RouteEntry? pathOnly = null;
        foreach (var entry in _entries)
        {
            if (!TryMatch(entry, path, out var values))
            {
                continue;
            }

            if (string.Equals(entry.Route.Method, method, StringComparison.Ordinal))
            {
                return new RouteMatch(entry.Route, values);
            }

            pathOnly ??= entry;
        }

        return pathOnly is null ? null : new RouteMatch(pathOnly.Route, new Dictionary<string, string>());
    }

    public IReadOnlyList<string> AllowedMethods(string path)
    {
        path = NormalizePath(path);
        var methods = new List<string>();
        foreach (var entry in _entries)
        {
            if (TryMatch(entry, path, out _) && !methods.Contains(entry.Route.Method, StringComparer.Ordinal))
            {
                methods.Add(entry.Route.Method);
            }
        }

        return methods;
    }

    private static bool TryMatch(RouteEntry entry, string path, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != entry.Segments.Length)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var expected = entry.Segments[index];
            var actual = segments[index];
            if (expected.IsParameter)
            {
                values[expected.Name] = Uri.UnescapeDataString(actual);
                continue;
            }

            if (!string.Equals(expected.Name, actual, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        var trimmed = path.Split('?', 2)[0];
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.Length > 1 ? trimmed.TrimEnd('/') : trimmed;
    }

    private static ImmutableArray<Segment> Parse(string template)
    {
        var parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var builder = ImmutableArray.CreateBuilder<Segment>(parts.Length);
        foreach (var part in parts)
        {
            if (part.Length >= 2 && part[0] == '{' && part[^1] == '}')
            {
                builder.Add(new Segment(part[1..^1], true));
            }
            else
            {
                builder.Add(new Segment(part, false));
            }
        }

        return builder.ToImmutable();
    }

    private readonly record struct Segment(string Name, bool IsParameter);

    private sealed class RouteEntry
    {
        public RouteEntry(RouteSymbol route, ImmutableArray<Segment> segments)
        {
            Route = route;
            Segments = segments;
            Specificity = segments.Count(segment => !segment.IsParameter) * 1000 + segments.Length;
        }

        public RouteSymbol Route { get; }

        public ImmutableArray<Segment> Segments { get; }

        public int Specificity { get; }
    }
}
