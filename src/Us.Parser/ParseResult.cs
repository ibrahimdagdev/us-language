using Us.Core;

namespace Us.Parser;

public sealed class ParseResult
{
    public ParseResult(CompilationUnitSyntax root, DiagnosticBag diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    public CompilationUnitSyntax Root { get; }

    public DiagnosticBag Diagnostics { get; }
}
