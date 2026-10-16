using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;

namespace Us.Lexer.Tests;

internal sealed class Lexed
{
    private Lexed(SourceFile file, LexerResult result)
    {
        File = file;
        Tokens = result.Tokens;
        Diagnostics = result.Diagnostics.ToImmutableArray();
    }

    public SourceFile File { get; }

    public ImmutableArray<Token> Tokens { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static Lexed Of(string text)
    {
        var file = SourceFile.FromText("ana.us", text);
        return new(file, Lexer.Tokenize(file));
    }

    public Token[] Significant() => Tokens.Where(token => token.Kind != TokenKind.EndOfFile).ToArray();

    public string TextOf(Token token) => File.Text.ToString(token.Span);

    public void Kinds(params TokenKind[] expected)
    {
        Assert.Equal(expected, Significant().Select(token => token.Kind));
    }
}
