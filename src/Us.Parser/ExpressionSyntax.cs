using System.Collections.Immutable;

namespace Us.Parser;

public abstract class ExpressionSyntax : SyntaxNodeBase
{
    protected ExpressionSyntax(params SyntaxNode?[] children)
        : base(List(children))
    {
    }

    protected ExpressionSyntax(ImmutableArray<SyntaxNode> children)
        : base(children)
    {
    }
}

public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    public LiteralExpressionSyntax(SyntaxToken token)
        : base(token)
    {
        Token = token;
    }

    public SyntaxToken Token { get; }

    public override SyntaxKind Kind => SyntaxKind.LiteralExpression;
}

public sealed class IdentifierExpressionSyntax : ExpressionSyntax
{
    public IdentifierExpressionSyntax(SyntaxToken name)
        : base(name)
    {
        Name = name;
    }

    public SyntaxToken Name { get; }

    public override SyntaxKind Kind => SyntaxKind.IdentifierExpression;
}

public sealed class CurrentItemExpressionSyntax : ExpressionSyntax
{
    public CurrentItemExpressionSyntax(SyntaxToken caret)
        : base(caret)
    {
        Caret = caret;
    }

    public SyntaxToken Caret { get; }

    public override SyntaxKind Kind => SyntaxKind.CurrentItemExpression;
}

public sealed class ThisExpressionSyntax : ExpressionSyntax
{
    public ThisExpressionSyntax(SyntaxToken token)
        : base(token)
    {
        Token = token;
    }

    public SyntaxToken Token { get; }

    public override SyntaxKind Kind => SyntaxKind.ThisExpression;
}

public sealed class BaseExpressionSyntax : ExpressionSyntax
{
    public BaseExpressionSyntax(SyntaxToken token)
        : base(token)
    {
        Token = token;
    }

    public SyntaxToken Token { get; }

    public override SyntaxKind Kind => SyntaxKind.BaseExpression;
}

public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    public ParenthesizedExpressionSyntax(SyntaxToken open, ExpressionSyntax expression, SyntaxToken close)
        : base(open, expression, close)
    {
        OpenParen = open;
        Expression = expression;
        CloseParen = close;
    }

    public SyntaxToken OpenParen { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken CloseParen { get; }

    public override SyntaxKind Kind => SyntaxKind.ParenthesizedExpression;
}

public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    public UnaryExpressionSyntax(SyntaxToken op, ExpressionSyntax operand)
        : base(op, operand)
    {
        Operator = op;
        Operand = operand;
    }

    public UnaryExpressionSyntax(ExpressionSyntax operand, SyntaxToken op)
        : base(operand, op)
    {
        Operator = op;
        Operand = operand;
        IsPostfix = true;
    }

    public SyntaxToken Operator { get; }

    public ExpressionSyntax Operand { get; }

    public bool IsPostfix { get; }

    public override SyntaxKind Kind => SyntaxKind.UnaryExpression;
}

public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    public BinaryExpressionSyntax(ExpressionSyntax left, SyntaxToken op, ExpressionSyntax right)
        : base(left, op, right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public ExpressionSyntax Left { get; }

    public SyntaxToken Operator { get; }

    public ExpressionSyntax Right { get; }

    public override SyntaxKind Kind => SyntaxKind.BinaryExpression;
}

public sealed class AssignmentExpressionSyntax : ExpressionSyntax
{
    public AssignmentExpressionSyntax(ExpressionSyntax left, SyntaxToken op, ExpressionSyntax right)
        : base(left, op, right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public ExpressionSyntax Left { get; }

    public SyntaxToken Operator { get; }

    public ExpressionSyntax Right { get; }

    public override SyntaxKind Kind => SyntaxKind.AssignmentExpression;
}

public sealed class MemberAccessExpressionSyntax : ExpressionSyntax
{
    public MemberAccessExpressionSyntax(ExpressionSyntax expression, SyntaxToken op, SyntaxToken name)
        : base(expression, op, name)
    {
        Expression = expression;
        Operator = op;
        Name = name;
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken Operator { get; }

    public SyntaxToken Name { get; }

    public override SyntaxKind Kind => SyntaxKind.MemberAccessExpression;
}

public sealed class ArgumentSyntax : SyntaxNodeBase
{
    public ArgumentSyntax(SyntaxToken? modifier, ExpressionSyntax expression, SyntaxToken? comma)
        : base(List(modifier, expression, comma))
    {
        Modifier = modifier;
        Expression = expression;
        Comma = comma;
    }

    public SyntaxToken? Modifier { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? Comma { get; }

    public override SyntaxKind Kind => SyntaxKind.Argument;
}

public sealed class ArgumentListSyntax : SyntaxNodeBase
{
    public ArgumentListSyntax(SyntaxToken open, ImmutableArray<ArgumentSyntax> arguments, SyntaxToken close)
        : base(List(Combine(open, arguments, close)))
    {
        OpenParen = open;
        Arguments = arguments;
        CloseParen = close;
    }

    public SyntaxToken OpenParen { get; }

    public ImmutableArray<ArgumentSyntax> Arguments { get; }

    public SyntaxToken CloseParen { get; }

    public override SyntaxKind Kind => SyntaxKind.ArgumentList;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken open, ImmutableArray<ArgumentSyntax> arguments, SyntaxToken close)
    {
        yield return open;
        foreach (var argument in arguments)
        {
            yield return argument;
        }

        yield return close;
    }
}

public sealed class InvocationExpressionSyntax : ExpressionSyntax
{
    public InvocationExpressionSyntax(ExpressionSyntax expression, TypeArgumentListSyntax? typeArguments, ArgumentListSyntax arguments)
        : base(List(expression, typeArguments, arguments))
    {
        Expression = expression;
        TypeArguments = typeArguments;
        Arguments = arguments;
    }

    public ExpressionSyntax Expression { get; }

    public TypeArgumentListSyntax? TypeArguments { get; }

    public ArgumentListSyntax Arguments { get; }

    public override SyntaxKind Kind => SyntaxKind.InvocationExpression;
}

public sealed class IndexExpressionSyntax : ExpressionSyntax
{
    public IndexExpressionSyntax(ExpressionSyntax expression, SyntaxToken open, ExpressionSyntax index, SyntaxToken close)
        : base(expression, open, index, close)
    {
        Expression = expression;
        OpenBracket = open;
        Index = index;
        CloseBracket = close;
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OpenBracket { get; }

    public ExpressionSyntax Index { get; }

    public SyntaxToken CloseBracket { get; }

    public override SyntaxKind Kind => SyntaxKind.IndexExpression;
}

public sealed class InterpolationHoleSyntax : SyntaxNodeBase
{
    public InterpolationHoleSyntax(ExpressionSyntax expression)
        : base(List(expression))
    {
        Expression = expression;
    }

    public ExpressionSyntax Expression { get; }

    public override SyntaxKind Kind => SyntaxKind.InterpolationHole;
}

public sealed class InterpolationExpressionSyntax : ExpressionSyntax
{
    public InterpolationExpressionSyntax(ImmutableArray<SyntaxNode> pieces)
        : base(pieces)
    {
        Pieces = pieces;
    }

    public ImmutableArray<SyntaxNode> Pieces { get; }

    public override SyntaxKind Kind => SyntaxKind.InterpolationExpression;
}

public sealed class ArrayExpressionSyntax : ExpressionSyntax
{
    public ArrayExpressionSyntax(SyntaxToken open, ImmutableArray<ExpressionSyntax> elements, ImmutableArray<SyntaxToken> commas, SyntaxToken close)
        : base(List(Combine(open, elements, commas, close)))
    {
        OpenBracket = open;
        Elements = elements;
        Commas = commas;
        CloseBracket = close;
    }

    public SyntaxToken OpenBracket { get; }

    public ImmutableArray<ExpressionSyntax> Elements { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public SyntaxToken CloseBracket { get; }

    public override SyntaxKind Kind => SyntaxKind.ArrayExpression;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken open, ImmutableArray<ExpressionSyntax> elements, ImmutableArray<SyntaxToken> commas, SyntaxToken close)
    {
        yield return open;
        for (var i = 0; i < elements.Length; i++)
        {
            yield return elements[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return close;
    }
}

public sealed class InitializerSyntax : SyntaxNodeBase
{
    public InitializerSyntax(SyntaxToken name, SyntaxToken equals, ExpressionSyntax value)
        : base(List(name, equals, value))
    {
        Name = name;
        EqualsToken = equals;
        Value = value;
    }

    public SyntaxToken Name { get; }

    public SyntaxToken EqualsToken { get; }

    public ExpressionSyntax Value { get; }

    public override SyntaxKind Kind => SyntaxKind.Initializer;
}

public sealed class ObjectCreationExpressionSyntax : ExpressionSyntax
{
    public ObjectCreationExpressionSyntax(SyntaxToken at, TypeSyntax type, ArgumentListSyntax? arguments, SyntaxToken? open, ImmutableArray<InitializerSyntax> initializers, SyntaxToken? close)
        : base(List(Combine(at, type, arguments, open, initializers, close)))
    {
        At = at;
        Type = type;
        Arguments = arguments;
        OpenBrace = open;
        Initializers = initializers;
        CloseBrace = close;
    }

    public SyntaxToken At { get; }

    public TypeSyntax Type { get; }

    public ArgumentListSyntax? Arguments { get; }

    public SyntaxToken? OpenBrace { get; }

    public ImmutableArray<InitializerSyntax> Initializers { get; }

    public SyntaxToken? CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.ObjectCreationExpression;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken at, TypeSyntax type, ArgumentListSyntax? arguments, SyntaxToken? open, ImmutableArray<InitializerSyntax> initializers, SyntaxToken? close)
    {
        yield return at;
        yield return type;
        yield return arguments;
        yield return open;
        foreach (var initializer in initializers)
        {
            yield return initializer;
        }

        yield return close;
    }
}

public sealed class CopyExpressionSyntax : ExpressionSyntax
{
    public CopyExpressionSyntax(ExpressionSyntax source, SyntaxToken ile, SyntaxToken open, ImmutableArray<InitializerSyntax> initializers, SyntaxToken close)
        : base(List(Combine(source, ile, open, initializers, close)))
    {
        Source = source;
        Ile = ile;
        OpenBrace = open;
        Initializers = initializers;
        CloseBrace = close;
    }

    public ExpressionSyntax Source { get; }

    public SyntaxToken Ile { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<InitializerSyntax> Initializers { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.CopyExpression;

    private static IEnumerable<SyntaxNode?> Combine(ExpressionSyntax source, SyntaxToken ile, SyntaxToken open, ImmutableArray<InitializerSyntax> initializers, SyntaxToken close)
    {
        yield return source;
        yield return ile;
        yield return open;
        foreach (var initializer in initializers)
        {
            yield return initializer;
        }

        yield return close;
    }
}

public sealed class ExpressionIfSyntax : ExpressionSyntax
{
    public ExpressionIfSyntax(SyntaxToken eger, ExpressionSyntax condition, SyntaxToken arrow, ExpressionSyntax whenTrue, SyntaxToken falseArrow, ExpressionSyntax whenFalse)
        : base(eger, condition, arrow, whenTrue, falseArrow, whenFalse)
    {
        Eger = eger;
        Condition = condition;
        Arrow = arrow;
        WhenTrue = whenTrue;
        FalseArrow = falseArrow;
        WhenFalse = whenFalse;
    }

    public SyntaxToken Eger { get; }

    public ExpressionSyntax Condition { get; }

    public SyntaxToken Arrow { get; }

    public ExpressionSyntax WhenTrue { get; }

    public SyntaxToken FalseArrow { get; }

    public ExpressionSyntax WhenFalse { get; }

    public override SyntaxKind Kind => SyntaxKind.ExpressionIf;
}

public sealed class AwaitExpressionSyntax : ExpressionSyntax
{
    public AwaitExpressionSyntax(SyntaxToken bekle, ExpressionSyntax expression)
        : base(bekle, expression)
    {
        Bekle = bekle;
        Expression = expression;
    }

    public SyntaxToken Bekle { get; }

    public ExpressionSyntax Expression { get; }

    public override SyntaxKind Kind => SyntaxKind.AwaitExpression;
}

public sealed class LambdaExpressionSyntax : ExpressionSyntax
{
    public LambdaExpressionSyntax(SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, ImmutableArray<SyntaxToken> commas, SyntaxToken close, SyntaxToken arrow, SyntaxNode body)
        : base(List(Combine(open, parameters, commas, close, arrow, body)))
    {
        OpenParen = open;
        Parameters = parameters;
        Commas = commas;
        CloseParen = close;
        Arrow = arrow;
        Body = body;
    }

    public SyntaxToken OpenParen { get; }

    public ImmutableArray<ParameterSyntax> Parameters { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public SyntaxToken CloseParen { get; }

    public SyntaxToken Arrow { get; }

    public SyntaxNode Body { get; }

    public override SyntaxKind Kind => SyntaxKind.LambdaExpression;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, ImmutableArray<SyntaxToken> commas, SyntaxToken close, SyntaxToken arrow, SyntaxNode body)
    {
        yield return open;
        for (var i = 0; i < parameters.Length; i++)
        {
            yield return parameters[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return close;
        yield return arrow;
        yield return body;
    }
}
