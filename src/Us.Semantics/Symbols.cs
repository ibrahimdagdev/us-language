using System.Collections.Immutable;
using Us.Core;

namespace Us.Semantics;

public enum SymbolKind
{
    Type,
    Variable,
    Parameter,
    Function,
    Field,
    Event,
    OptionMember,
    Namespace,
    Service,
    Database,
    Table,
    Mapping,
    Test,
    Controller,
    Route,
}

public enum Visibility
{
    Public,
    Internal,
    Protected,
    Private,
}

public abstract class Symbol
{
    private protected Symbol(string name, SymbolKind kind)
    {
        Name = name;
        Kind = kind;
    }

    public string Name { get; }

    public SymbolKind Kind { get; }

    public Symbol? Containing { get; internal set; }
}

public abstract class TypeSymbol : Symbol
{
    private protected TypeSymbol(string name, bool isNullable)
        : base(name, SymbolKind.Type)
    {
        IsNullable = isNullable;
    }

    public bool IsNullable { get; }

    public virtual string DisplayName => IsNullable && Name.Length > 0 ? Name + "?" : Name;

    public abstract TypeSymbol WithNullable(bool nullable);
}

public sealed class PrimitiveTypeSymbol : TypeSymbol
{
    public PrimitiveTypeSymbol(string name)
        : base(name, false)
    {
        Canonical = this;
        NullableForm = new PrimitiveTypeSymbol(name, this);
    }

    private PrimitiveTypeSymbol(string name, PrimitiveTypeSymbol canonical)
        : base(name, true)
    {
        Canonical = canonical;
        NullableForm = this;
    }

    public PrimitiveTypeSymbol Canonical { get; }

    public PrimitiveTypeSymbol NullableForm { get; }

    public override TypeSymbol WithNullable(bool nullable) => nullable ? NullableForm : Canonical;
}

public sealed class ErrorTypeSymbol : TypeSymbol
{
    public static readonly ErrorTypeSymbol Instance = new();

    private ErrorTypeSymbol()
        : base("hata-tipi", false)
    {
    }

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class UnitTypeSymbol : TypeSymbol
{
    public static readonly UnitTypeSymbol Instance = new();

    private UnitTypeSymbol()
        : base(string.Empty, false)
    {
    }

    public override string DisplayName => string.Empty;

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class NullTypeSymbol : TypeSymbol
{
    public static readonly NullTypeSymbol Instance = new();

    private NullTypeSymbol()
        : base("~", true)
    {
    }

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class TypeParameterSymbol : TypeSymbol
{
    public TypeParameterSymbol(string name)
        : base(name, false)
    {
        Canonical = this;
        NullableForm = new TypeParameterSymbol(this);
    }

    private TypeParameterSymbol(TypeParameterSymbol canonical)
        : base(canonical.Name, true)
    {
        Canonical = canonical;
        NullableForm = this;
    }

    public TypeParameterSymbol Canonical { get; }

    public TypeParameterSymbol NullableForm { get; }

    public List<TypeSymbol> Constraints { get; } = [];

    public override TypeSymbol WithNullable(bool nullable) => nullable ? (Canonical ?? this).NullableForm : Canonical ?? this;
}

public sealed class ConstructedTypeSymbol : TypeSymbol
{
    public ConstructedTypeSymbol(TypeSymbol definition, ImmutableArray<TypeSymbol> arguments, bool isNullable)
        : base(definition.Name, isNullable)
    {
        Definition = definition;
        Arguments = arguments;
    }

    public TypeSymbol Definition { get; }

    public ImmutableArray<TypeSymbol> Arguments { get; }

    public override string DisplayName
    {
        get
        {
            var text = Name + "<" + string.Join(", ", Arguments.Select(argument => argument.DisplayName)) + ">";
            return IsNullable ? text + "?" : text;
        }
    }

    public override TypeSymbol WithNullable(bool nullable) =>
        nullable == IsNullable ? this : new ConstructedTypeSymbol(Definition, Arguments, nullable);
}

public sealed class SourceTypeSymbol : TypeSymbol
{
    public SourceTypeSymbol(string name, SourceTypeKind typeKind)
        : base(name, false)
    {
        Canonical = this;
        TypeKind = typeKind;
        NullableForm = new SourceTypeSymbol(this);
    }

    private SourceTypeSymbol(SourceTypeSymbol canonical)
        : base(canonical.Name, true)
    {
        Canonical = canonical;
        TypeKind = canonical.TypeKind;
        NullableForm = this;
    }

    public SourceTypeSymbol Canonical { get; }

    public SourceTypeSymbol NullableForm { get; }

    public SourceTypeKind TypeKind { get; }

    public bool IsAbstract { get; internal set; }

    public bool IsSealed { get; internal set; }

    public Visibility Visibility { get; internal set; } = Visibility.Internal;

    public TypeSymbol? BaseType { get; internal set; }

    public List<TypeSymbol> Interfaces { get; } = [];

    public List<TypeParameterSymbol> TypeParameters { get; } = [];

    public List<FieldSymbol> Fields { get; } = [];

    public List<FunctionSymbol> Methods { get; } = [];

    public List<EventSymbol> Events { get; } = [];

    public string? ConfigSection { get; internal set; }

    public ServiceLifetime? ServiceLifetime { get; internal set; }

    public List<Symbol> OptionMembers { get; } = [];

    public override TypeSymbol WithNullable(bool nullable) => nullable ? (Canonical ?? this).NullableForm : Canonical ?? this;

    public SourceTypeSymbol Definition => Canonical ?? this;
}

public enum SourceTypeKind
{
    Model,
    Class,
    Interface,
    Option,
}

public enum ServiceLifetime
{
    Scoped,
    Singleton,
    Transient,
}

public sealed class ServiceRegistration
{
    public ServiceRegistration(SourceTypeSymbol serviceType, ServiceLifetime lifetime, FunctionSymbol? constructor, TextSpan span)
    {
        ServiceType = serviceType.Definition;
        Lifetime = lifetime;
        Constructor = constructor;
        Span = span;
    }

    public SourceTypeSymbol ServiceType { get; }

    public SourceTypeSymbol ImplementationType => ServiceType;

    public ServiceLifetime Lifetime { get; }

    public FunctionSymbol? Constructor { get; }

    public TextSpan Span { get; }
}

public sealed class VariableSymbol : Symbol
{
    public VariableSymbol(string name, TypeSymbol type)
        : base(name, SymbolKind.Variable)
    {
        Type = type;
    }

    public TypeSymbol Type { get; }

    public FunctionSymbol? Callable { get; internal set; }
}

public sealed class ParameterSymbol : Symbol
{
    public ParameterSymbol(string name, TypeSymbol type, bool isRef, bool isOut, bool hasDefault)
        : base(name, SymbolKind.Parameter)
    {
        Type = type;
        IsRef = isRef;
        IsOut = isOut;
        HasDefault = hasDefault;
    }

    public TypeSymbol Type { get; }

    public bool IsRef { get; }

    public bool IsOut { get; }

    public bool HasDefault { get; }

    public BoundNode? Default { get; internal set; }
}

public sealed class FunctionSymbol : Symbol
{
    public FunctionSymbol(string name)
        : base(name, SymbolKind.Function)
    {
    }

    public List<ParameterSymbol> Parameters { get; } = [];

    public List<TypeParameterSymbol> TypeParameters { get; } = [];

    public TypeSymbol ReturnType { get; internal set; } = UnitTypeSymbol.Instance;

    public bool IsAsync { get; internal set; }

    public bool IsConstructor { get; internal set; }

    public bool IsVirtual { get; internal set; }

    public bool IsOverride { get; internal set; }

    public bool IsAbstract { get; internal set; }

    public bool IsStatic { get; internal set; }

    public bool HasBody { get; internal set; }

    public Visibility Visibility { get; internal set; } = Visibility.Internal;

    public SourceTypeSymbol? DeclaringType { get; internal set; }

    public BoundNode? BoundBody { get; internal set; }

    public FunctionSymbol? Template { get; internal set; }

    public FunctionSymbol? OverrideOf { get; internal set; }

    public List<FunctionSymbol> Targets { get; } = [];

    public bool IsLambda { get; internal set; }

    public Intrinsic Intrinsic { get; internal set; }
}

public sealed class DatabaseSymbol : Symbol
{
    public DatabaseSymbol(string name)
        : base(name, SymbolKind.Database)
    {
    }

    public List<TableSymbol> Tables { get; } = [];
}

public sealed class ControllerSymbol : Symbol
{
    public ControllerSymbol(string name, string prefix, TextSpan span)
        : base(name, SymbolKind.Controller)
    {
        Prefix = prefix;
        Span = span;
    }

    public string Prefix { get; }

    public TextSpan Span { get; }

    public List<RouteSymbol> Routes { get; } = [];

    public List<BoundNode> Setup { get; } = [];
}

public sealed class RouteSymbol : Symbol
{
    public RouteSymbol(string method, string template, string structuralKey, ControllerSymbol controller, FunctionSymbol handler, TextSpan span)
        : base(handler.Name, SymbolKind.Route)
    {
        Method = method;
        Template = template;
        StructuralKey = structuralKey;
        Controller = controller;
        Handler = handler;
        Span = span;
        Parameters = [.. ExtractParameters(template)];
    }

    public string Method { get; }

    public string Template { get; }

    public string StructuralKey { get; }

    public ControllerSymbol Controller { get; }

    public FunctionSymbol Handler { get; }

    public TextSpan Span { get; }

    public ImmutableArray<string> Parameters { get; }

    private static IEnumerable<string> ExtractParameters(string template)
    {
        foreach (var segment in template.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}')
            {
                yield return segment[1..^1];
            }
        }
    }
}

public sealed class TableSymbol : Symbol
{
    public TableSymbol(string name, TypeSymbol model, DatabaseSymbol database)
        : base(name, SymbolKind.Table)
    {
        Model = model;
        Database = database;
    }

    public TypeSymbol Model { get; }

    public DatabaseSymbol Database { get; }
}

public sealed class DatabaseTypeSymbol : TypeSymbol
{
    public DatabaseTypeSymbol(DatabaseSymbol database)
        : base(database.Name, false)
    {
        Database = database;
    }

    public DatabaseSymbol Database { get; }

    public override string DisplayName => Database.Name;

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class TableQueryTypeSymbol : TypeSymbol
{
    public TableQueryTypeSymbol(TableSymbol table)
        : base(table.Name, false)
    {
        Table = table;
    }

    public TableSymbol Table { get; }

    public TypeSymbol Element => Table.Model;

    public override string DisplayName => Table.Name;

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class HttpResultTypeSymbol : TypeSymbol
{
    public HttpResultTypeSymbol()
        : base("HttpSonuç", false)
    {
    }

    public override string DisplayName => "HttpSonuç";

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class HttpBuilderTypeSymbol : TypeSymbol
{
    public HttpBuilderTypeSymbol()
        : base("Httpİstek", false)
    {
    }

    public override string DisplayName => "Httpİstek";

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class HttpResponseTypeSymbol : TypeSymbol
{
    public HttpResponseTypeSymbol()
        : base("HttpYanıt", false)
    {
    }

    public override string DisplayName => "HttpYanıt";

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class ModuleSymbol : Symbol
{
    public ModuleSymbol(string name)
        : base(name, SymbolKind.Namespace)
    {
    }

    public List<FunctionSymbol> Methods { get; } = [];
}

public sealed class FieldSymbol : Symbol
{
    public FieldSymbol(string name, TypeSymbol type, Visibility visibility)
        : base(name, SymbolKind.Field)
    {
        Type = type;
        Visibility = visibility;
    }

    public TypeSymbol Type { get; }

    public Visibility Visibility { get; }

    public SourceTypeSymbol? DeclaringType { get; internal set; }

    public BoundNode? Default { get; internal set; }

    public bool IsRequired { get; internal set; }

    public bool IsUnique { get; internal set; }

    public bool IsKey { get; internal set; }

    public BoundNode? MaxLength { get; internal set; }

    public BoundNode? RangeMinimum { get; internal set; }

    public BoundNode? RangeMaximum { get; internal set; }
}

public sealed class EventSymbol : Symbol
{
    public EventSymbol(string name, TypeSymbol type, Visibility visibility)
        : base(name, SymbolKind.Event)
    {
        Type = type;
        Visibility = visibility;
    }

    public TypeSymbol Type { get; }

    public Visibility Visibility { get; }

    public SourceTypeSymbol? DeclaringType { get; internal set; }
}

public sealed class GenericDefinitionSymbol : TypeSymbol
{
    public GenericDefinitionSymbol(string name, int arity)
        : base(name, false)
    {
        Arity = arity;
    }

    public int Arity { get; }

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class MethodGroupTypeSymbol : TypeSymbol
{
    public MethodGroupTypeSymbol(IReadOnlyList<FunctionSymbol> functions)
        : base(functions.Count > 0 ? functions[0].Name : string.Empty, false)
    {
        Functions = functions;
    }

    public IReadOnlyList<FunctionSymbol> Functions { get; }

    public override TypeSymbol WithNullable(bool nullable) => this;
}

public sealed class MappingSymbol : Symbol
{
    public MappingSymbol(TypeSymbol source, TypeSymbol destination)
        : base("dönüşüm", SymbolKind.Mapping)
    {
        Source = source;
        Destination = destination;
    }

    public TypeSymbol Source { get; }

    public TypeSymbol Destination { get; }

    public List<(FieldSymbol Field, BoundNode Value)> Assignments { get; } = [];
}

public sealed class TestSymbol : Symbol
{
    public TestSymbol(string name, TextSpan span, BoundNode body, bool isAsync)
        : base(name, SymbolKind.Test)
    {
        Span = span;
        Body = body;
        IsAsync = isAsync;
    }

    public TextSpan Span { get; }

    public BoundNode Body { get; }

    public bool IsAsync { get; }
}

public sealed class Scope
{
    private readonly Dictionary<string, List<Symbol>> _symbols = new(StringComparer.Ordinal);

    public Scope(Scope? parent)
    {
        Parent = parent;
    }

    public Scope? Parent { get; }

    public bool TryDeclare(Symbol symbol)
    {
        if (!_symbols.TryGetValue(symbol.Name, out var list))
        {
            _symbols[symbol.Name] = [symbol];
            return true;
        }

        if (symbol is FunctionSymbol && list.All(item => item is FunctionSymbol))
        {
            list.Add(symbol);
            return true;
        }

        return false;
    }

    public Symbol? FindLocal(string name) =>
        _symbols.TryGetValue(name, out var list) ? list[0] : null;

    public IReadOnlyList<Symbol> FindLocalAll(string name) =>
        _symbols.TryGetValue(name, out var list) ? list : [];

    public IReadOnlyList<Symbol> Lookup(string name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            var found = scope.FindLocalAll(name);
            if (found.Count > 0)
            {
                return found;
            }
        }

        return [];
    }
}
