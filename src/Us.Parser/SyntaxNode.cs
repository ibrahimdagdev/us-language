using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;

namespace Us.Parser;

public abstract class SyntaxNode
{
    public abstract SyntaxKind Kind { get; }

    public abstract TextSpan Span { get; }

    public abstract IReadOnlyList<SyntaxNode> Children { get; }

    public static TextSpan ComputeSpan(IReadOnlyList<SyntaxNode> children)
    {
        if (children.Count == 0)
        {
            return new TextSpan(0, 0);
        }

        var start = children[0].Span.Start;
        var end = children[0].Span.End;
        for (var i = 1; i < children.Count; i++)
        {
            var span = children[i].Span;
            if (span.Start < start)
            {
                start = span.Start;
            }

            if (span.End > end)
            {
                end = span.End;
            }
        }

        return TextSpan.FromBounds(start, end);
    }

    protected static ImmutableArray<SyntaxNode> List(params SyntaxNode?[] children)
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxNode>(children.Length);
        foreach (var child in children)
        {
            if (child is not null)
            {
                builder.Add(child);
            }
        }

        return builder.ToImmutable();
    }

    protected static ImmutableArray<SyntaxNode> List(IEnumerable<SyntaxNode?> children)
    {
        var builder = ImmutableArray.CreateBuilder<SyntaxNode>();
        foreach (var child in children)
        {
            if (child is not null)
            {
                builder.Add(child);
            }
        }

        return builder.ToImmutable();
    }
}

public abstract class SyntaxNodeBase : SyntaxNode
{
    protected SyntaxNodeBase(ImmutableArray<SyntaxNode> children)
    {
        Children = children;
        Span = ComputeSpan(children);
    }

    public override TextSpan Span { get; }

    public override IReadOnlyList<SyntaxNode> Children { get; }
}

public sealed class SyntaxToken : SyntaxNode
{
    public SyntaxToken(Token token, bool isMissing)
    {
        Token = token;
        IsMissing = isMissing;
    }

    public Token Token { get; }

    public bool IsMissing { get; }

    public TokenKind TokenKind => Token.Kind;

    public override SyntaxKind Kind => SyntaxKind.Token;

    public override TextSpan Span => Token.Span;

    public override IReadOnlyList<SyntaxNode> Children => [];

    public static SyntaxToken Missing(TokenKind kind, int position) =>
        new(new Token(kind, new TextSpan(position, 0), ImmutableArray<Trivia>.Empty), true);
}
