using Us.Core;

namespace Us.Lexer;

public readonly record struct Trivia(TriviaKind Kind, TextSpan Span);
