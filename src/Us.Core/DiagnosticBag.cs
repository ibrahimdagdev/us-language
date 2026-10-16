using System.Collections.Immutable;

namespace Us.Core;

public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];

    public bool HasErrors => _items.Exists(item => item.Severity == DiagnosticSeverity.Error);

    public void Add(Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _items.Add(diagnostic);
    }

    public ImmutableArray<Diagnostic> ToImmutableArray() => [.. _items];
}
