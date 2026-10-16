using System.Collections.Immutable;

namespace Us.Parser;

public sealed class ConstraintSyntax : SyntaxNodeBase
{
    public ConstraintSyntax(SyntaxToken name, ArgumentListSyntax? arguments, SyntaxToken? comma)
        : base(List(name, arguments, comma))
    {
        Name = name;
        Arguments = arguments;
        Comma = comma;
    }

    public SyntaxToken Name { get; }

    public ArgumentListSyntax? Arguments { get; }

    public SyntaxToken? Comma { get; }

    public override SyntaxKind Kind => SyntaxKind.Constraint;
}

public sealed class ModelFieldSyntax : SyntaxNodeBase
{
    public ModelFieldSyntax(ImmutableArray<SyntaxToken> modifiers, ImmutableArray<SyntaxToken> names, ImmutableArray<SyntaxToken> commas, SyntaxToken colon, TypeSyntax type, SyntaxToken? ruleColon, ImmutableArray<ConstraintSyntax> constraints)
        : base(List(Combine(modifiers, names, commas, colon, type, ruleColon, constraints)))
    {
        Modifiers = modifiers;
        Names = names;
        NameCommas = commas;
        Colon = colon;
        Type = type;
        RuleColon = ruleColon;
        Constraints = constraints;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public ImmutableArray<SyntaxToken> Names { get; }

    public ImmutableArray<SyntaxToken> NameCommas { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? RuleColon { get; }

    public ImmutableArray<ConstraintSyntax> Constraints { get; }

    public override SyntaxKind Kind => SyntaxKind.ModelField;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, ImmutableArray<SyntaxToken> names, ImmutableArray<SyntaxToken> commas, SyntaxToken colon, TypeSyntax type, SyntaxToken? ruleColon, ImmutableArray<ConstraintSyntax> constraints)
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
        yield return ruleColon;
        foreach (var constraint in constraints)
        {
            yield return constraint;
        }
    }
}

public sealed class ModelDeclarationSyntax : StatementSyntax
{
    public ModelDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken open, ImmutableArray<ModelFieldSyntax> fields, SyntaxToken close, SyntaxToken? colon = null, SyntaxToken? settingName = null, SyntaxToken? settingOpen = null, SyntaxToken? settingArgument = null, SyntaxToken? settingClose = null)
        : base(List(Combine(keyword, name, typeParameters, colon, settingName, settingOpen, settingArgument, settingClose, open, fields, close)))
    {
        Keyword = keyword;
        Name = name;
        TypeParameters = typeParameters;
        OpenBrace = open;
        Fields = fields;
        CloseBrace = close;
        Colon = colon;
        SettingName = settingName;
        SettingOpen = settingOpen;
        SettingArgument = settingArgument;
        SettingClose = settingClose;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public TypeParameterListSyntax? TypeParameters { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<ModelFieldSyntax> Fields { get; }

    public SyntaxToken CloseBrace { get; }

    public SyntaxToken? Colon { get; }

    public SyntaxToken? SettingName { get; }

    public SyntaxToken? SettingOpen { get; }

    public SyntaxToken? SettingArgument { get; }

    public SyntaxToken? SettingClose { get; }

    public override SyntaxKind Kind => SyntaxKind.ModelDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken? colon, SyntaxToken? settingName, SyntaxToken? settingOpen, SyntaxToken? settingArgument, SyntaxToken? settingClose, SyntaxToken open, ImmutableArray<ModelFieldSyntax> fields, SyntaxToken close)
    {
        yield return keyword;
        yield return name;
        yield return typeParameters;
        yield return colon;
        yield return settingName;
        yield return settingOpen;
        yield return settingArgument;
        yield return settingClose;
        yield return open;
        foreach (var field in fields)
        {
            yield return field;
        }

        yield return close;
    }
}

public sealed class ClassDeclarationSyntax : StatementSyntax
{
    public ClassDeclarationSyntax(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken? colon, ImmutableArray<TypeSyntax> bases, ImmutableArray<SyntaxToken> commas, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
        : base(List(Combine(modifiers, keyword, name, typeParameters, colon, bases, commas, open, members, close)))
    {
        Modifiers = modifiers;
        Keyword = keyword;
        Name = name;
        TypeParameters = typeParameters;
        Colon = colon;
        Bases = bases;
        BaseCommas = commas;
        OpenBrace = open;
        Members = members;
        CloseBrace = close;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public TypeParameterListSyntax? TypeParameters { get; }

    public SyntaxToken? Colon { get; }

    public ImmutableArray<TypeSyntax> Bases { get; }

    public ImmutableArray<SyntaxToken> BaseCommas { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SyntaxNode> Members { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.ClassDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken name, TypeParameterListSyntax? typeParameters, SyntaxToken? colon, ImmutableArray<TypeSyntax> bases, ImmutableArray<SyntaxToken> commas, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
    {
        foreach (var modifier in modifiers)
        {
            yield return modifier;
        }

        yield return keyword;
        yield return name;
        yield return typeParameters;
        yield return colon;
        for (var i = 0; i < bases.Length; i++)
        {
            yield return bases[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return open;
        foreach (var member in members)
        {
            yield return member;
        }

        yield return close;
    }
}

public sealed class ConstructorDeclarationSyntax : StatementSyntax
{
    public ConstructorDeclarationSyntax(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, SyntaxToken close, SyntaxNode body)
        : base(List(Combine(modifiers, keyword, open, parameters, close, body)))
    {
        Modifiers = modifiers;
        Keyword = keyword;
        OpenParen = open;
        Parameters = parameters;
        CloseParen = close;
        Body = body;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public SyntaxToken Keyword { get; }

    public SyntaxToken OpenParen { get; }

    public ImmutableArray<ParameterSyntax> Parameters { get; }

    public SyntaxToken CloseParen { get; }

    public SyntaxNode Body { get; }

    public override SyntaxKind Kind => SyntaxKind.ConstructorDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken open, ImmutableArray<ParameterSyntax> parameters, SyntaxToken close, SyntaxNode body)
    {
        foreach (var modifier in modifiers)
        {
            yield return modifier;
        }

        yield return keyword;
        yield return open;
        foreach (var parameter in parameters)
        {
            yield return parameter;
        }

        yield return close;
        yield return body;
    }
}

public sealed class InterfaceDeclarationSyntax : StatementSyntax
{
    public InterfaceDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
        : base(List(Combine(keyword, name, open, members, close)))
    {
        Keyword = keyword;
        Name = name;
        OpenBrace = open;
        Members = members;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SyntaxNode> Members { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.InterfaceDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
    {
        yield return keyword;
        yield return name;
        yield return open;
        foreach (var member in members)
        {
            yield return member;
        }

        yield return close;
    }
}

public sealed class OptionDeclarationSyntax : StatementSyntax
{
    public OptionDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<SyntaxToken> members, ImmutableArray<SyntaxToken> commas, SyntaxToken close)
        : base(List(Combine(keyword, name, open, members, commas, close)))
    {
        Keyword = keyword;
        Name = name;
        OpenBrace = open;
        Members = members;
        Commas = commas;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SyntaxToken> Members { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.OptionDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<SyntaxToken> members, ImmutableArray<SyntaxToken> commas, SyntaxToken close)
    {
        yield return keyword;
        yield return name;
        yield return open;
        for (var i = 0; i < members.Length; i++)
        {
            yield return members[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return close;
    }
}

public sealed class SelectArmSyntax : SyntaxNodeBase
{
    public SelectArmSyntax(ExpressionSyntax pattern, bool isWildcard, SyntaxToken arrow, StatementSyntax body)
        : base(List(pattern, arrow, body))
    {
        Pattern = pattern;
        IsWildcard = isWildcard;
        Arrow = arrow;
        Body = body;
    }

    public ExpressionSyntax Pattern { get; }

    public bool IsWildcard { get; }

    public SyntaxToken Arrow { get; }

    public StatementSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.SelectArm;
}

public sealed class SelectStatementSyntax : StatementSyntax
{
    public SelectStatementSyntax(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken open, ImmutableArray<SelectArmSyntax> arms, SyntaxToken close)
        : base(List(Combine(keyword, expression, open, arms, close)))
    {
        Keyword = keyword;
        Expression = expression;
        OpenBrace = open;
        Arms = arms;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SelectArmSyntax> Arms { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.SelectStatement;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken open, ImmutableArray<SelectArmSyntax> arms, SyntaxToken close)
    {
        yield return keyword;
        yield return expression;
        yield return open;
        foreach (var arm in arms)
        {
            yield return arm;
        }

        yield return close;
    }
}

public sealed class CatchClauseSyntax : SyntaxNodeBase
{
    public CatchClauseSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken? colon, TypeSyntax? type, BlockSyntax body)
        : base(List(keyword, name, colon, type, body))
    {
        Keyword = keyword;
        Name = name;
        Colon = colon;
        Type = type;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken? Colon { get; }

    public TypeSyntax? Type { get; }

    public BlockSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.CatchClause;
}

public sealed class TryStatementSyntax : StatementSyntax
{
    public TryStatementSyntax(SyntaxToken? preparation, bool preparationIsUnderscore, BlockSyntax? preparationBody, SyntaxToken? dene, BlockSyntax? body, ImmutableArray<CatchClauseSyntax> catches, SyntaxToken? sonunda, BlockSyntax? finallyBody)
        : base(List(Combine(preparation, preparationBody, dene, body, catches, sonunda, finallyBody)))
    {
        Preparation = preparation;
        PreparationIsUnderscore = preparationIsUnderscore;
        PreparationBody = preparationBody;
        Dene = dene;
        Body = body;
        Catches = catches;
        Sonunda = sonunda;
        Finally = finallyBody;
    }

    public SyntaxToken? Preparation { get; }

    public bool PreparationIsUnderscore { get; }

    public BlockSyntax? PreparationBody { get; }

    public SyntaxToken? Dene { get; }

    public BlockSyntax? Body { get; }

    public ImmutableArray<CatchClauseSyntax> Catches { get; }

    public SyntaxToken? Sonunda { get; }

    public BlockSyntax? Finally { get; }

    public override SyntaxKind Kind => SyntaxKind.TryStatement;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken? preparation, BlockSyntax? preparationBody, SyntaxToken? dene, BlockSyntax? body, ImmutableArray<CatchClauseSyntax> catches, SyntaxToken? sonunda, BlockSyntax? finallyBody)
    {
        yield return preparation;
        yield return preparationBody;
        yield return dene;
        yield return body;
        foreach (var clause in catches)
        {
            yield return clause;
        }

        yield return sonunda;
        yield return finallyBody;
    }
}

public sealed class ThrowStatementSyntax : StatementSyntax
{
    public ThrowStatementSyntax(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken? semicolon)
        : base(keyword, expression, semicolon)
    {
        Keyword = keyword;
        Expression = expression;
        Semicolon = semicolon;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.ThrowStatement;
}

public sealed class EventDeclarationSyntax : StatementSyntax
{
    public EventDeclarationSyntax(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken name, SyntaxToken colon, TypeSyntax type, SyntaxToken? semicolon)
        : base(List(Combine(modifiers, keyword, name, colon, type, semicolon)))
    {
        Modifiers = modifiers;
        Keyword = keyword;
        Name = name;
        Colon = colon;
        Type = type;
        Semicolon = semicolon;
    }

    public ImmutableArray<SyntaxToken> Modifiers { get; }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.EventDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<SyntaxToken> modifiers, SyntaxToken keyword, SyntaxToken name, SyntaxToken colon, TypeSyntax type, SyntaxToken? semicolon)
    {
        foreach (var modifier in modifiers)
        {
            yield return modifier;
        }

        yield return keyword;
        yield return name;
        yield return colon;
        yield return type;
        yield return semicolon;
    }
}

public sealed class ConcurrentStatementSyntax : StatementSyntax
{
    public ConcurrentStatementSyntax(SyntaxToken keyword, BlockSyntax body)
        : base(keyword, body)
    {
        Keyword = keyword;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public BlockSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.ConcurrentStatement;
}

public sealed class RuleClauseSyntax : SyntaxNodeBase
{
    public RuleClauseSyntax(SyntaxToken keyword, ExpressionSyntax condition, SyntaxToken arrow, ExpressionSyntax message)
        : base(List(keyword, condition, arrow, message))
    {
        Keyword = keyword;
        Condition = condition;
        Arrow = arrow;
        Message = message;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Condition { get; }

    public SyntaxToken Arrow { get; }

    public ExpressionSyntax Message { get; }

    public override SyntaxKind Kind => SyntaxKind.RuleClause;
}

public sealed class ValidationStatementSyntax : StatementSyntax
{
    public ValidationStatementSyntax(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken open, ImmutableArray<RuleClauseSyntax> rules, SyntaxToken close)
        : base(List(Combine(keyword, expression, open, rules, close)))
    {
        Keyword = keyword;
        Expression = expression;
        OpenBrace = open;
        Rules = rules;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<RuleClauseSyntax> Rules { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.ValidationStatement;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken open, ImmutableArray<RuleClauseSyntax> rules, SyntaxToken close)
    {
        yield return keyword;
        yield return expression;
        yield return open;
        foreach (var rule in rules)
        {
            yield return rule;
        }

        yield return close;
    }
}

public sealed class MappingDeclarationSyntax : StatementSyntax
{
    public MappingDeclarationSyntax(SyntaxToken keyword, TypeSyntax source, SyntaxToken arrow, TypeSyntax destination, SyntaxToken open, ImmutableArray<InitializerSyntax> members, SyntaxToken close)
        : base(List(CombineAll(keyword, source, arrow, destination, open, members, close)))
    {
        Keyword = keyword;
        Source = source;
        Arrow = arrow;
        Destination = destination;
        OpenBrace = open;
        Members = members;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public TypeSyntax Source { get; }

    public SyntaxToken Arrow { get; }

    public TypeSyntax Destination { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<InitializerSyntax> Members { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.MappingDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(ImmutableArray<InitializerSyntax> members)
    {
        foreach (var member in members)
        {
            yield return member;
        }
    }

    private static IEnumerable<SyntaxNode?> CombineAll(SyntaxToken keyword, TypeSyntax source, SyntaxToken arrow, TypeSyntax destination, SyntaxToken open, ImmutableArray<InitializerSyntax> members, SyntaxToken close)
    {
        yield return keyword;
        yield return source;
        yield return arrow;
        yield return destination;
        yield return open;
        foreach (var member in members)
        {
            yield return member;
        }

        yield return close;
    }
}

public sealed class NamespaceDeclarationSyntax : StatementSyntax
{
    public NamespaceDeclarationSyntax(SyntaxToken keyword, ImmutableArray<SyntaxToken> name, ImmutableArray<SyntaxToken> dots, SyntaxToken? colon, SyntaxToken? visibility, SyntaxToken? semicolon)
        : base(List(Combine(keyword, name, dots, colon, visibility, semicolon)))
    {
        Keyword = keyword;
        Name = name;
        Dots = dots;
        Colon = colon;
        Visibility = visibility;
        Semicolon = semicolon;
    }

    public SyntaxToken Keyword { get; }

    public ImmutableArray<SyntaxToken> Name { get; }

    public ImmutableArray<SyntaxToken> Dots { get; }

    public SyntaxToken? Colon { get; }

    public SyntaxToken? Visibility { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.NamespaceDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, ImmutableArray<SyntaxToken> name, ImmutableArray<SyntaxToken> dots, SyntaxToken? colon, SyntaxToken? visibility, SyntaxToken? semicolon)
    {
        yield return keyword;
        for (var i = 0; i < name.Length; i++)
        {
            yield return name[i];
            if (i < dots.Length)
            {
                yield return dots[i];
            }
        }

        yield return colon;
        yield return visibility;
        yield return semicolon;
    }
}

public sealed class RouteDeclarationSyntax : SyntaxNodeBase
{
    public RouteDeclarationSyntax(SyntaxToken keyword, SyntaxToken verb, SyntaxNode path)
        : base(List(keyword, verb, path))
    {
        Keyword = keyword;
        Verb = verb;
        Path = path;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Verb { get; }

    public SyntaxNode Path { get; }

    public override SyntaxKind Kind => SyntaxKind.RouteDeclaration;
}

public sealed class ImportDeclarationSyntax : StatementSyntax
{
    public ImportDeclarationSyntax(SyntaxToken keyword, ImmutableArray<SyntaxToken> name, ImmutableArray<SyntaxToken> dots, SyntaxToken? olarak, SyntaxToken? alias, SyntaxToken? semicolon)
        : base(List(Combine(keyword, name, dots, olarak, alias, semicolon)))
    {
        Keyword = keyword;
        Name = name;
        Dots = dots;
        Olarak = olarak;
        Alias = alias;
        Semicolon = semicolon;
    }

    public SyntaxToken Keyword { get; }

    public ImmutableArray<SyntaxToken> Name { get; }

    public ImmutableArray<SyntaxToken> Dots { get; }

    public SyntaxToken? Olarak { get; }

    public SyntaxToken? Alias { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.ImportDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, ImmutableArray<SyntaxToken> name, ImmutableArray<SyntaxToken> dots, SyntaxToken? olarak, SyntaxToken? alias, SyntaxToken? semicolon)
    {
        yield return keyword;
        for (var i = 0; i < name.Length; i++)
        {
            yield return name[i];
            if (i < dots.Length)
            {
                yield return dots[i];
            }
        }

        yield return olarak;
        yield return alias;
        yield return semicolon;
    }
}

public sealed class TableDeclarationSyntax : SyntaxNodeBase
{
    public TableDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken colon, TypeSyntax type)
        : base(List(keyword, name, colon, type))
    {
        Keyword = keyword;
        Name = name;
        Colon = colon;
        Type = type;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public override SyntaxKind Kind => SyntaxKind.TableDeclaration;
}

public sealed class DatabaseDeclarationSyntax : StatementSyntax
{
    public DatabaseDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<TableDeclarationSyntax> tables, SyntaxToken close)
        : base(List(Combine(keyword, name, open, tables, close)))
    {
        Keyword = keyword;
        Name = name;
        OpenBrace = open;
        Tables = tables;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<TableDeclarationSyntax> Tables { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.DatabaseDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, SyntaxToken name, SyntaxToken open, ImmutableArray<TableDeclarationSyntax> tables, SyntaxToken close)
    {
        yield return keyword;
        yield return name;
        yield return open;
        foreach (var table in tables)
        {
            yield return table;
        }

        yield return close;
    }
}

public sealed class TransactionStatementSyntax : StatementSyntax
{
    public TransactionStatementSyntax(SyntaxToken keyword, BlockSyntax body)
        : base(keyword, body)
    {
        Keyword = keyword;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public BlockSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.TransactionStatement;
}

public sealed class CommitStatementSyntax : StatementSyntax
{
    public CommitStatementSyntax(SyntaxToken keyword, SyntaxToken? semicolon)
        : base(keyword, semicolon)
    {
        Keyword = keyword;
    }

    public SyntaxToken Keyword { get; }

    public override SyntaxKind Kind => SyntaxKind.CommitStatement;
}

public sealed class RollbackStatementSyntax : StatementSyntax
{
    public RollbackStatementSyntax(SyntaxToken keyword, SyntaxToken? semicolon)
        : base(keyword, semicolon)
    {
        Keyword = keyword;
    }

    public SyntaxToken Keyword { get; }

    public override SyntaxKind Kind => SyntaxKind.RollbackStatement;
}

public sealed class ControllerDeclarationSyntax : StatementSyntax
{
    public ControllerDeclarationSyntax(SyntaxToken keyword, SyntaxToken name, SyntaxToken colon, SyntaxToken route, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
        : base(List(Combine(keyword, name, colon, route, open, members, close)))
    {
        Keyword = keyword;
        Name = name;
        Colon = colon;
        Route = route;
        OpenBrace = open;
        Members = members;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken Name { get; }

    public SyntaxToken Colon { get; }

    public SyntaxToken Route { get; }

    public SyntaxToken OpenBrace { get; }

    public ImmutableArray<SyntaxNode> Members { get; }

    public SyntaxToken CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.ControllerDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, SyntaxToken name, SyntaxToken colon, SyntaxToken route, SyntaxToken open, ImmutableArray<SyntaxNode> members, SyntaxToken close)
    {
        yield return keyword;
        yield return name;
        yield return colon;
        yield return route;
        yield return open;
        foreach (var member in members)
        {
            yield return member;
        }

        yield return close;
    }
}

public sealed class ServiceDeclarationSyntax : StatementSyntax
{
    public ServiceDeclarationSyntax(SyntaxToken keyword, TypeSyntax type, SyntaxToken? colon, SyntaxToken? lifetime, SyntaxToken? open, ImmutableArray<SyntaxNode> members, SyntaxToken? close)
        : base(List(Combine(keyword, type, colon, lifetime, open, members, close)))
    {
        Keyword = keyword;
        Type = type;
        Colon = colon;
        Lifetime = lifetime;
        OpenBrace = open;
        Members = members;
        CloseBrace = close;
    }

    public SyntaxToken Keyword { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? Colon { get; }

    public SyntaxToken? Lifetime { get; }

    public SyntaxToken? OpenBrace { get; }

    public ImmutableArray<SyntaxNode> Members { get; }

    public SyntaxToken? CloseBrace { get; }

    public override SyntaxKind Kind => SyntaxKind.ServiceDeclaration;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken keyword, TypeSyntax type, SyntaxToken? colon, SyntaxToken? lifetime, SyntaxToken? open, ImmutableArray<SyntaxNode> members, SyntaxToken? close)
    {
        yield return keyword;
        yield return type;
        yield return colon;
        yield return lifetime;
        yield return open;
        foreach (var member in members)
        {
            yield return member;
        }

        yield return close;
    }
}

public sealed class TestDeclarationSyntax : StatementSyntax
{
    public TestDeclarationSyntax(SyntaxToken keyword, ExpressionSyntax name, BlockSyntax body)
        : base(keyword, name, body)
    {
        Keyword = keyword;
        Name = name;
        Body = body;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Name { get; }

    public BlockSyntax Body { get; }

    public override SyntaxKind Kind => SyntaxKind.TestDeclaration;
}

public sealed class AssertionStatementSyntax : StatementSyntax
{
    public AssertionStatementSyntax(SyntaxToken keyword, ExpressionSyntax expression, SyntaxToken? semicolon)
        : base(keyword, expression, semicolon)
    {
        Keyword = keyword;
        Expression = expression;
        Semicolon = semicolon;
    }

    public SyntaxToken Keyword { get; }

    public ExpressionSyntax Expression { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.AssertionStatement;
}

public sealed class BindingStatementSyntax : StatementSyntax
{
    public BindingStatementSyntax(SyntaxToken keyword, SyntaxToken kind, SyntaxToken colon, TypeSyntax type, SyntaxToken? semicolon)
        : base(keyword, kind, colon, type, semicolon)
    {
        Keyword = keyword;
        KindName = kind;
        Colon = colon;
        Type = type;
        Semicolon = semicolon;
    }

    public SyntaxToken Keyword { get; }

    public SyntaxToken KindName { get; }

    public SyntaxToken Colon { get; }

    public TypeSyntax Type { get; }

    public SyntaxToken? Semicolon { get; }

    public override SyntaxKind Kind => SyntaxKind.BindingStatement;
}
