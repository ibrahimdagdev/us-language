using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;

namespace Us.Semantics;

public enum BoundKind
{
    Compilation,
    Block,
    Local,
    Expression,
    Literal,
    Name,
    Unary,
    Binary,
    Assignment,
    Call,
    Member,
    Error,
    Result,
    If,
    Loop,
    Jump,
    Type,
    Other,
}

public sealed class BoundNode
{
    public BoundNode(BoundKind kind, TextSpan span, TypeSymbol type, Symbol? symbol, ImmutableArray<BoundNode> children, bool flag = false, TokenKind? op = null, string? constant = null)
    {
        Kind = kind;
        Span = span;
        Type = type;
        Symbol = symbol;
        Children = children;
        Flag = flag;
        Operator = op;
        Constant = constant;
    }

    public BoundKind Kind { get; }

    public TextSpan Span { get; }

    public TypeSymbol Type { get; }

    public Symbol? Symbol { get; }

    public ImmutableArray<BoundNode> Children { get; }

    public bool Flag { get; }

    public TokenKind? Operator { get; }

    public string? Constant { get; }

    public BoundNode With(TokenKind? op = null, string? constant = null, bool? flag = null) =>
        new(Kind, Span, Type, Symbol, Children, flag ?? Flag, op ?? Operator, constant ?? Constant);

    public static BoundNode Leaf(BoundKind kind, TextSpan span, TypeSymbol type, Symbol? symbol = null, bool flag = false, TokenKind? op = null, string? constant = null) =>
        new(kind, span, type, symbol, ImmutableArray<BoundNode>.Empty, flag, op, constant);

    public static BoundNode Create(BoundKind kind, TextSpan span, TypeSymbol type, Symbol? symbol, bool flag, params BoundNode?[] children)
    {
        var builder = ImmutableArray.CreateBuilder<BoundNode>(children.Length);
        foreach (var child in children)
        {
            if (child is not null)
            {
                builder.Add(child);
            }
        }

        return new BoundNode(kind, span, type, symbol, builder.ToImmutable(), flag);
    }
}

public sealed class BoundCompilation
{
    public BoundCompilation(
        ImmutableArray<BoundNode> members,
        DiagnosticBag diagnostics,
        ImmutableArray<TestSymbol> tests,
        ImmutableArray<ServiceRegistration> services,
        ImmutableArray<DatabaseSymbol> databases,
        ImmutableArray<ControllerSymbol> controllers,
        ImmutableArray<RouteSymbol> routes,
        ImmutableArray<FunctionSymbol> functions = default)
    {
        Members = members;
        Diagnostics = diagnostics;
        Tests = tests;
        Services = services;
        Databases = databases;
        Controllers = controllers;
        Routes = routes;
        Functions = functions.IsDefault ? ImmutableArray<FunctionSymbol>.Empty : functions;
    }

    public ImmutableArray<BoundNode> Members { get; }

    public DiagnosticBag Diagnostics { get; }

    public ImmutableArray<TestSymbol> Tests { get; }

    public ImmutableArray<ServiceRegistration> Services { get; }

    public ImmutableArray<DatabaseSymbol> Databases { get; }

    public ImmutableArray<ControllerSymbol> Controllers { get; }

    public ImmutableArray<RouteSymbol> Routes { get; }

    public ImmutableArray<FunctionSymbol> Functions { get; }
}
