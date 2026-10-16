namespace Us.Semantics;

public static class Conversion
{
    public static bool Same(TypeSymbol left, TypeSymbol right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.IsNullable != right.IsNullable)
        {
            return false;
        }

        left = Strip(left);
        right = Strip(right);
        if (left is PrimitiveTypeSymbol primitive && right is PrimitiveTypeSymbol other)
        {
            return ReferenceEquals(primitive.Canonical, other.Canonical);
        }

        if (left is SourceTypeSymbol source && right is SourceTypeSymbol target)
        {
            return ReferenceEquals(source.Definition, target.Definition);
        }

        if (left is TypeParameterSymbol parameter && right is TypeParameterSymbol otherParameter)
        {
            return ReferenceEquals(parameter.Canonical ?? parameter, otherParameter.Canonical ?? otherParameter);
        }

        if (left is ConstructedTypeSymbol constructed && right is ConstructedTypeSymbol otherConstructed)
        {
            if (!Same(constructed.Definition.WithNullable(false), otherConstructed.Definition.WithNullable(false)) &&
                !ReferenceEquals(DefinitionOf(constructed), DefinitionOf(otherConstructed)))
            {
                return false;
            }

            if (constructed.Arguments.Length != otherConstructed.Arguments.Length)
            {
                return false;
            }

            for (var i = 0; i < constructed.Arguments.Length; i++)
            {
                if (!Same(constructed.Arguments[i], otherConstructed.Arguments[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    public static bool IsImplicit(TypeSymbol from, TypeSymbol to)
    {
        if (from is ErrorTypeSymbol || to is ErrorTypeSymbol || from is NullTypeSymbol && to.IsNullable)
        {
            return true;
        }

        if (to is PrimitiveTypeSymbol dynamicTarget && dynamicTarget.Canonical.Name == "dinamik")
        {
            return true;
        }

        if (to is PrimitiveTypeSymbol objectTarget && objectTarget.Canonical.Name == "nesne" && from is not NullTypeSymbol)
        {
            return true;
        }

        if (Same(from, to))
        {
            return true;
        }

        if (!from.IsNullable && to.IsNullable && IsImplicit(from, to.WithNullable(false)))
        {
            return true;
        }

        if (from.IsNullable || to.IsNullable)
        {
            return false;
        }

        var source = from.DisplayName;
        var target = to.DisplayName;
        if (source == "tamsayı" && target is "ondalık" or "gondalık" or "hondalık")
        {
            return true;
        }

        return source == "utamsayı" && target == "ondalık" || Derives(from, to);
    }

    public static TypeSymbol Common(TypeSymbol left, TypeSymbol right)
    {
        if (IsImplicit(left, right))
        {
            return right;
        }

        if (IsImplicit(right, left))
        {
            return left;
        }

        return ErrorTypeSymbol.Instance;
    }

    public static bool Derives(TypeSymbol type, TypeSymbol baseType)
    {
        var current = Strip(type) as SourceTypeSymbol;
        var target = Strip(baseType) as SourceTypeSymbol;
        if (current is null || target is null)
        {
            return false;
        }

        current = current.Definition;
        target = target.Definition;
        var guard = 0;
        while (current is not null && guard++ < 32)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            if (current.Interfaces.Any(item => Same(Strip(item), target)))
            {
                return true;
            }

            current = current.BaseType is SourceTypeSymbol next ? next.Definition : null;
        }

        return false;
    }

    public static TypeSymbol Strip(TypeSymbol type) => type.WithNullable(false);

    private static TypeSymbol DefinitionOf(ConstructedTypeSymbol type) =>
        type.Definition is SourceTypeSymbol source ? source.Definition : type.Definition.WithNullable(false);
}
