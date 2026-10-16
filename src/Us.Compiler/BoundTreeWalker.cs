using System.Collections.Immutable;
using Us.Core;
using Us.Semantics;

namespace Us.Compiler;

public sealed class BoundWalkResult
{
    public BoundWalkResult(ImmutableArray<Diagnostic> diagnostics, int nodeCount)
    {
        Diagnostics = diagnostics;
        NodeCount = nodeCount;
    }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public int NodeCount { get; }

    public bool HasErrors => Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
}

public static class BoundTreeWalker
{
    public static BoundWalkResult Walk(BoundCompilation compilation, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var diagnostics = new List<Diagnostic>();
        var count = 0;
        foreach (var member in compilation.Members)
        {
            WalkNode(member, filePath, diagnostics, ref count);
        }

        foreach (var test in compilation.Tests)
        {
            if (test.Body is not null)
            {
                WalkNode(test.Body, filePath, diagnostics, ref count);
            }
        }

        foreach (var function in compilation.Functions)
        {
            if (function.BoundBody is not null)
            {
                WalkNode(function.BoundBody, filePath, diagnostics, ref count);
            }
        }

        return new BoundWalkResult([.. diagnostics], count);
    }

    private static void WalkNode(BoundNode node, string? filePath, List<Diagnostic> diagnostics, ref int count)
    {
        count++;
        if (node.Kind == BoundKind.Name && node.Symbol is null && node.Operator is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnresolvedBound,
                DiagnosticSeverity.Error,
                "Bağlanmış ağaçta çözülmemiş ad düğümü var.",
                filePath,
                node.Span,
                1,
                1,
                "Bağlayıcı çıktısını kullanın; sözdizimini yeniden çözmeyin."));
        }

        if (node.Kind == BoundKind.Error)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnresolvedBound,
                DiagnosticSeverity.Error,
                "Bağlanmış ağaçta hata düğümü var.",
                filePath,
                node.Span,
                1,
                1,
                null));
        }

        foreach (var child in node.Children)
        {
            WalkNode(child, filePath, diagnostics, ref count);
        }
    }
}
