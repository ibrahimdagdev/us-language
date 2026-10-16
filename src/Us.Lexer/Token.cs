using System.Collections.Immutable;
using Us.Core;

namespace Us.Lexer;

public readonly record struct Token(TokenKind Kind, TextSpan Span, ImmutableArray<Trivia> LeadingTrivia)
{
    public static Token Create(TokenKind kind, TextSpan span, ImmutableArray<Trivia> leadingTrivia) =>
        new(kind, span, leadingTrivia.IsDefault ? ImmutableArray<Trivia>.Empty : leadingTrivia);
}
