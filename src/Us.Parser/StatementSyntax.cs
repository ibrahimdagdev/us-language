using System.Collections.Immutable;

namespace Us.Parser;

public abstract class StatementSyntax : SyntaxNodeBase
{
    protected StatementSyntax(params SyntaxNode?[] children)
        : base(List(children))
    {
    }

    protected StatementSyntax(ImmutableArray<SyntaxNode> children)
        : base(children)
    {
    }
}

public sealed class CompilationUnitSyntax : SyntaxNodeBase
{
    public CompilationUnitSyntax(ImmutableArray<SyntaxNode> members, SyntaxToken endOfFile)
        : base(List(Combine(members, endOfFile)))
    {
        Members = members;
        EndOfFile = endOfFile;
    }

    public ImmutableArray<SyntaxNode> Members { get; }

    public SyntaxToken EndOfFile { get; }

    public override SyntaxKind Kind => SyntaxKind.CompilationUnit;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxNode> members, SyntaxToken endOfFile)
    {
        foreach (var member in members)
        {
            yield return member;
        }

        yield return endOfFile;
    }
}

public sealed class BlockSyntax : StatementSyntax
{
    public BlockSyntax(SyntaxToken open, ImmutableArray<SyntaxNode> statements, SyntaxToken close)
        : base(List(Combine(open, statements, close)))
    {
        OpenBrace = open;
        Statements = statements;
        CloseBrace = close;
    }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SyntaxNode> Statements { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.Block;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken open, ImmutableArray<SyntaxNode> statements, SyntaxToken close)
    {
        yield return open;
        foreach (var statement in statements)
        {
            yield return statement;
        }

        yield return close;
    }
}

public sealed class ErrorStatementSyntax : StatementSyntax
{
    public ErrorStatementSyntax(SyntaxToken token)
        : base(token)
    {
        Token = token;
    }

    public SyntaxToken Token { get; }

    public override SyntaxKind Kind => SyntaxKind.ErrorStatement;
}

public sealed class EmptyStatementSyntax : StatementSyntax
{
    public EmptyStatementSyntax(SyntaxToken semicolon)
        : base(semicolon)
    {
        Semicolon = semicolon;
    }

    public SyntaxToken Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.EmptyStatement;
}

public sealed class ExpressionStatementSyntax : StatementSyntax
{
    public ExpressionStatementSyntax(ExpressionSyntax expression, SyntaxToken? semicolon)
        : base(expression, semicolon)
    {
        Expression = expression;
        Semicolon = semicolon;
    }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.ExpressionStatement;
}

public sealed class ResultStatementSyntax : StatementSyntax
{
    public ResultStatementSyntax(SyntaxToken arrow, ExpressionSyntax expression, SyntaxToken? semicolon)
        : base(arrow, expression, semicolon)
    {
        Arrow = arrow;
        Expression = expression;
        Semicolon = semicolon;
    }

    public SyntaxToken Arrow { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.ResultStatement;
}

public sealed class VariableDeclarationSyntax : StatementSyntax
{
    public VariableDeclarationSyntax(ImmutableArray<SyntaxToken> modifiers, ImmutableArray<SyntaxToken> names, ImmutableArray<SyntaxToken> commas, SyntaxToken colon, TypeSyntax type, SyntaxToken? equals, ImmutableArray<ExpressionSyntax> initializers, ImmutableArray<SyntaxToken> initializerCommas, SyntaxToken? semicolon)
        : base(List(Combine(modifiers, names, commas, colon, type, equals, initializers, initializerCommas, semicolon)))
    {
        Modifiers = modifiers;
        Names = names;
        NameCommas = commas;
        Colon = colon;
        Type = type;
        EqualsToken = equals;
        Initializers = initializers;
        InitializerCommas = initializerCommas;
        Semicolon = semicolon;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public ImmutableArray<SyntaxToken> Names { get; }

    public ImmutableArray<SyntaxToken> NameCommas { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? EqualsToken { get; }

    public ImmutableArray<ExpressionSyntax> Initializers { get; }

    public ImmutableArray<SyntaxToken> InitializerCommas { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.VariableDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, ImmutableArray<SyntaxToken> names, ImmutableArray<SyntaxToken> commas, SyntaxToken colon, TypeSyntax type, SyntaxToken? equals, ImmutableArray<ExpressionSyntax> initializers, ImmutableArray<SyntaxToken> initializerCommas, SyntaxToken? semicolon)
    {
        foreach (var modifier in modifiers)
        {
            yield return modifier;
        }

        for (var i = 0; i < names.Length; i++)
        {
            yield return names[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return colon;
        yield return type;
        yield return equals;
        for (var i = 0; i < initializers.Length; i++)
        {
            yield return initializers[i];
            if (i < initializerCommas.Length)
            {
                yield return initializerCommas[i];
            }
        }

        yield return semicolon;
    }
}

public sealed class ElseIfClauseSyntax : SyntaxNodeBase
{
    public ElseIfClauseSyntax(SyntaxToken keyword, ExpressionSyntax condition, StatementSyntax body)
        : base(List(keyword, condition, body))
    {
        Keyword = keyword;
        Condition = condition;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Condition { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.ElseIfClause;
}

public sealed class IfStatementSyntax : StatementSyntax
{
    public IfStatementSyntax(SyntaxToken eger, ExpressionSyntax condition, StatementSyntax body, ImmutableArray<ElseIfClauseSyntax> elseIfs, SyntaxToken? degilse, StatementSyntax? elseBody)
        : base(List(Combine(eger, condition, body, elseIfs, degilse, elseBody)))
    {
        Eger = eger;
        Condition = condition;
        Body = body;
        ElseIfs = elseIfs;
        Degilse = degilse;
        ElseBody = elseBody;
    }

    public SyntaxToken Eger { get; }

    public ExpressionSyntax Condition { get; }

    public StatementSyntax Body { get; }

    public ImmutableArray<ElseIfClauseSyntax> ElseIfs { get; }

    public SyntaxToken? Degilse { get; }

    public StatementSyntax? ElseBody { get; }

    public override SyntaxKind Kind => SyntaxKind.IfStatement;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken eger, ExpressionSyntax condition, StatementSyntax body, ImmutableArray<ElseIfClauseSyntax> elseIfs, SyntaxToken? degilse, StatementSyntax? elseBody)
    {
        yield return eger;
        yield return condition;
        yield return body;
        foreach (var clause in elseIfs)
        {
            yield return clause;
        }

        yield return degilse;
        yield return elseBody;
    }
}

public sealed class BreakStatementSyntax : StatementSyntax
{
    public BreakStatementSyntax(SyntaxToken keyword, SyntaxToken? semicolon)
        : base(keyword, semicolon)
    {
        Keyword = keyword;
    }

    public SyntaxToken Keyword { get; }

    public override SyntaxKind Kind => SyntaxKind.BreakStatement;
}

public sealed class ContinueStatementSyntax : StatementSyntax
{
    public ContinueStatementSyntax(SyntaxToken keyword, SyntaxToken? semicolon)
        : base(keyword, semicolon)
    {
        Keyword = keyword;
    }

    public SyntaxToken Keyword { get; }

    public override SyntaxKind Kind => SyntaxKind.ContinueStatement;
}

public sealed class ForEachStatementSyntax : StatementSyntax
{
    public ForEachStatementSyntax(SyntaxToken don, ExpressionSyntax collection, SyntaxToken arrow, SyntaxToken name, StatementSyntax body)
        : base(don, collection, arrow, name, body)
    {
        Don = don;
        Collection = collection;
        Arrow = arrow;
        Name = name;
        Body = body;
    }

    public SyntaxToken Don { get; }

    public ExpressionSyntax Collection { get; }

    public SyntaxToken Arrow { get; }

    public SyntaxToken Name { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.ForEachStatement;
}

public sealed class RangeForStatementSyntax : StatementSyntax
{
    public RangeForStatementSyntax(SyntaxToken don, ExpressionSyntax start, SyntaxToken ile, ExpressionSyntax end, SyntaxToken? adimKeyword, ExpressionSyntax? step, SyntaxToken arrow, SyntaxToken name, StatementSyntax body)
        : base(don, start, ile, end, adimKeyword, step, arrow, name, body)
    {
        Don = don;
        Start = start;
        Ile = ile;
        End = end;
        Adim = adimKeyword;
        Step = step;
        Arrow = arrow;
        Name = name;
        Body = body;
    }

    public SyntaxToken Don { get; }

    public ExpressionSyntax Start { get; }

    public SyntaxToken Ile { get; }

    public ExpressionSyntax End { get; }

    public SyntaxToken? Adim { get; }

    public ExpressionSyntax? Step { get; }

    public SyntaxToken Arrow { get; }

    public SyntaxToken Name { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.RangeForStatement;
}

public sealed class WhileStatementSyntax : StatementSyntax
{
    public WhileStatementSyntax(SyntaxToken keyword, ExpressionSyntax condition, StatementSyntax body)
        : base(keyword, condition, body)
    {
        Keyword = keyword;
        Condition = condition;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Condition { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.WhileStatement;
}

public sealed class DoWhileStatementSyntax : StatementSyntax
{
    public DoWhileStatementSyntax(SyntaxToken yap, StatementSyntax body, SyntaxToken tekrarla, ExpressionSyntax condition)
        : base(yap, body, tekrarla, condition)
    {
        Yap = yap;
        Body = body;
        Tekrarla = tekrarla;
        Condition = condition;
    }

    public SyntaxToken Yap { get; }

    public StatementSyntax Body { get; }

    public SyntaxToken Tekrarla { get; }

    public ExpressionSyntax Condition { get; }

    public override SyntaxKind Kind => SyntaxKind.DoWhileStatement;
}

public sealed class InfiniteLoopStatementSyntax : StatementSyntax
{
    public InfiniteLoopStatementSyntax(SyntaxToken keyword, StatementSyntax body)
        : base(keyword, body)
    {
        Keyword = keyword;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.InfiniteLoopStatement;
}

public sealed class ParameterSyntax : SyntaxNodeBase
{
    public ParameterSyntax(SyntaxToken? modifier, SyntaxToken name, SyntaxToken colon, TypeSyntax type, SyntaxToken? equals, ExpressionSyntax? defaultValue, SyntaxToken? comma)
        : base(List(modifier, name, colon, type, equals, defaultValue, comma))
    {
        Modifier = modifier;
        Name = name;
        Colon = colon;
        Type = type;
        EqualsToken = equals;
        DefaultValue = defaultValue;
        Comma = comma;
    }

    public SyntaxToken? Modifier { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? EqualsToken { get; }

    public ExpressionSyntax? DefaultValue { get; }

    public SyntaxToken? Comma { get; }

    public override SyntaxKind Kind => SyntaxKind.Parameter;
}

public sealed class ExpectedErrorStatementSyntax : StatementSyntax
{
    public ExpectedErrorStatementSyntax(SyntaxToken keyword, BlockSyntax body)
        : base(keyword, body)
    {
        Keyword = keyword;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public BlockSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.ExpectedError;
}

public sealed class FunctionDeclarationSyntax : StatementSyntax
{
    public FunctionDeclarationSyntax(ImmutableArray<SyntaxToken> modifiers, RouteDeclarationSyntax? route, SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, SyntaxToken close, SyntaxToken? colon, TypeSyntax? returnType, SyntaxToken? arrow, SyntaxNode? body)
        : base(List(Combine(modifiers, route, keyword, name, typeParameters, open, parameters, close, colon, returnType, arrow, body)))
    {
        Modifiers = modifiers;
        Route = route;
        Keyword = keyword;
        Name = name;
        TypeParameters = typeParameters;
        OpenParen = open;
        Parameters = parameters;
        CloseParen = close;
        Colon = colon;
        ReturnType = returnType;
        Arrow = arrow;
        Body = body;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public RouteDeclarationSyntax? Route { get; }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public TypeParameterListSyntax? TypeParameters { get; }

    public SyntaxToken OpenParen { get; }

    public ImmutableArray<ParameterSyntax> Parameters { get; }

    public SyntaxToken CloseParen { get; }

    public SyntaxToken? Colon { get; }

    public TypeSyntax? ReturnType { get; }

    public SyntaxToken? Arrow { get; }

    public SyntaxNode? Body { get; }

    public override SyntaxKind Kind => SyntaxKind.FunctionDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, RouteDeclarationSyntax? route, SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, SyntaxToken close, SyntaxToken? colon, TypeSyntax? returnType, SyntaxToken? arrow, SyntaxNode? body)
    {
        foreach (var modifier in modifiers)
        {
            yield return modifier;
        }

        yield return route;
        yield return keyword;
        yield return name;
        yield return typeParameters;
        yield return open;
        foreach (var parameter in parameters)
        {
            yield return parameter;
        }

        yield return close;
        yield return colon;
        yield return returnType;
        yield return arrow;
        yield return body;
    }
}
