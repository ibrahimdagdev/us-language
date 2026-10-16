using System.Collections.Immutable;

namespace Us.Parser;

public sealed class TypeSyntax : SyntaxNodeBase
{
    public TypeSyntax(SyntaxToken name, TypeArgumentListSyntax? arguments, ImmutableArray<SyntaxToken> nullables)
        : base(List(Combine(name, arguments, nullables)))
    {
        Name = name;
        Arguments = arguments;
        Nullables = nullables;
    }

    public SyntaxToken Name { get; }

    public TypeArgumentListSyntax? Arguments { get; }

    public ImmutableArray<SyntaxToken> Nullables { get; }

    public override SyntaxKind Kind => SyntaxKind.TypeName;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken name, TypeArgumentListSyntax? arguments, ImmutableArray<SyntaxToken> nullables)
    {
        yield return name;
        yield return arguments;
        foreach (var token in nullables)
        {
            yield return token;
        }
    }
}

public sealed class TypeArgumentListSyntax : SyntaxNodeBase
{
    public TypeArgumentListSyntax(SyntaxToken less, ImmutableArray<TypeSyntax> arguments, ImmutableArray<SyntaxToken> commas, SyntaxToken greater)
        : base(List(Combine(less, arguments, commas, greater)))
    {
        Less = less;
        Arguments = arguments;
        Commas = commas;
        Greater = greater;
    }

    public SyntaxToken Less { get; }

    public ImmutableArray<TypeSyntax> Arguments { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public SyntaxToken Greater { get; }

    public override SyntaxKind Kind => SyntaxKind.TypeArgumentList;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken less, ImmutableArray<TypeSyntax> arguments, ImmutableArray<SyntaxToken> commas, SyntaxToken greater)
    {
        yield return less;
        for (var i = 0; i < arguments.Length; i++)
        {
            yield return arguments[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return greater;
    }
}

public sealed class TypeParameterSyntax : SyntaxNodeBase
{
    public TypeParameterSyntax(SyntaxToken name, SyntaxToken? colon, ImmutableArray<TypeSyntax> constraints, ImmutableArray<SyntaxToken> commas)
        : base(List(Combine(name, colon, constraints, commas)))
    {
        Name = name;
        Colon = colon;
        Constraints = constraints;
        Commas = commas;
    }

    public SyntaxToken Name { get; }

    public SyntaxToken? Colon { get; }

    public ImmutableArray<TypeSyntax> Constraints { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public override SyntaxKind Kind => SyntaxKind.TypeParameter;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken name, SyntaxToken? colon, ImmutableArray<TypeSyntax> constraints, ImmutableArray<SyntaxToken> commas)
    {
        yield return name;
        yield return colon;
        for (var i = 0; i < constraints.Length; i++)
        {
            yield return constraints[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }
    }
}

public sealed class TypeParameterListSyntax : SyntaxNodeBase
{
    public TypeParameterListSyntax(SyntaxToken less, ImmutableArray<TypeParameterSyntax> parameters, ImmutableArray<SyntaxToken> commas, SyntaxToken greater)
        : base(List(Combine(less, parameters, commas, greater)))
    {
        Less = less;
        Parameters = parameters;
        Commas = commas;
        Greater = greater;
    }

    public SyntaxToken Less { get; }

    public ImmutableArray<TypeParameterSyntax> Parameters { get; }

    public ImmutableArray<SyntaxToken> Commas { get; }

    public SyntaxToken Greater { get; }

    public override SyntaxKind Kind => SyntaxKind.TypeParameterList;

    private static IEnumerable<SyntaxNode?> Combine(SyntaxToken less, ImmutableArray<TypeParameterSyntax> parameters, ImmutableArray<SyntaxToken> commas, SyntaxToken greater)
    {
        yield return less;
        for (var i = 0; i < parameters.Length; i++)
        {
            yield return parameters[i];
            if (i < commas.Length)
            {
                yield return commas[i];
            }
        }

        yield return greater;
    }
}
