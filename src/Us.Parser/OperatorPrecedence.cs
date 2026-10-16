using Us.Lexer;

namespace Us.Parser;

public static class OperatorPrecedence
{
    public const int None = 0;
    public const int Assignment = 1;
    public const int NullCoalesce = 2;
    public const int Or = 3;
    public const int And = 4;
    public const int Equality = 5;
    public const int Comparison = 6;
    public const int Addition = 7;
    public const int Multiplication = 8;
    public const int Unary = 9;
    public const int Postfix = 10;

    public static int Infix(TokenKind kind) => kind switch
    {
        TokenKind.QuestionQuestion => NullCoalesce,
        TokenKind.PipePipe => Or,
        TokenKind.AmpersandAmpersand => And,
        TokenKind.EqualEqual or TokenKind.BangEqual => Equality,
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => Comparison,
        TokenKind.Plus or TokenKind.Minus => Addition,
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => Multiplication,
        _ => None,
    };

    public static bool IsAssignment(TokenKind kind) => kind is
        TokenKind.Equals or
        TokenKind.PlusEquals or
        TokenKind.MinusEquals or
        TokenKind.StarEquals or
        TokenKind.SlashEquals;
}
