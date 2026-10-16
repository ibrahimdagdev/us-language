using System.Collections.Immutable;
using Us.Core;

namespace Us.Lexer;

public sealed class LexerResult
{
    public LexerResult(ImmutableArray<Token> tokens, DiagnosticBag diagnostics)
    {
        Tokens = tokens;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<Token> Tokens { get; }

    public DiagnosticBag Diagnostics { get; }
}
