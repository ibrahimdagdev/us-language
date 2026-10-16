using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class UsInstance
{
    private readonly Dictionary<FieldSymbol, Cell> _fields = [];
    private readonly Dictionary<EventSymbol, List<UsHandler>> _events = [];

    private UsInstance(TypeSymbol runtimeType, SourceTypeSymbol definition)
    {
        RuntimeType = runtimeType;
        Definition = definition;
        IsSealed = definition.IsSealed;
    }

    public TypeSymbol RuntimeType { get; }

    public SourceTypeSymbol Definition { get; }

    public bool IsSealed { get; }

    public bool Released { get; internal set; }

    public void EnsureActive()
    {
        if (Released)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ScopeClosed, "Kapatılan kapsamın örneği kullanılamaz.", 0, 0);
        }
    }

    public static UsInstance Allocate(TypeSymbol type)
    {
        var definition = DefinitionOf(type) ?? throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Örnek tipi yok.", 0, 0);
        if (definition.IsAbstract)
        {
            throw new UsRuntimeException(UsRuntimeCodes.AbstractRuntime, $"'{definition.Name}' soyut olduğu için oluşturulamaz.", 0, 0);
        }

        var instance = new UsInstance(type, definition);
        foreach (var field in Fields(definition))
        {
            instance._fields[field] = new Cell(field.Type.IsNullable || field.DeclaringType?.TypeKind == SourceTypeKind.Model);
        }

        return instance;
    }

    public UsValue Read(FieldSymbol field, int start, int length)
    {
        if (!_fields.TryGetValue(field, out var cell))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{field.Name}' alanı yok.", start, length);
        }

        return cell.Read(start, length);
    }

    public void Write(FieldSymbol field, UsValue value, TypeSymbol runtimeType, int start, int length)
    {
        if (!_fields.TryGetValue(field, out var cell))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{field.Name}' alanı yok.", start, length);
        }

        var expected = Substitute(field.Type, runtimeType);
        if (expected is PrimitiveTypeSymbol && !value.IsNull && KindOf(expected) != value.Kind && !CanWiden(value.Kind, KindOf(expected)))
        {
            throw new UsRuntimeException(UsRuntimeCodes.GenericStore, $"'{field.Name}' alanına '{value.Kind}' yazılamaz.", start, length);
        }

        cell.Write(value, start, length);
    }

    public bool Assigned(FieldSymbol field) => _fields.TryGetValue(field, out var cell) && cell.IsAssigned;

    public List<UsHandler> Handlers(EventSymbol symbol)
    {
        if (!_events.TryGetValue(symbol, out var handlers))
        {
            handlers = [];
            _events[symbol] = handlers;
        }

        return handlers;
    }

    public Cell GetCell(FieldSymbol field) =>
        _fields.TryGetValue(field, out var cell) ? cell : throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{field.Name}' alanı yok.", 0, 0);

    public static IEnumerable<FieldSymbol> Fields(SourceTypeSymbol type)
    {
        var chain = new List<SourceTypeSymbol>();
        var seen = new HashSet<SourceTypeSymbol>();
        for (var current = type.Definition; current is not null && seen.Add(current); current = current.BaseType as SourceTypeSymbol)
        {
            chain.Add(current.Definition);
        }

        chain.Reverse();
        foreach (var item in chain)
        {
            foreach (var field in item.Fields)
            {
                yield return field;
            }
        }
    }

    public static SourceTypeSymbol? DefinitionOf(TypeSymbol type) => type switch
    {
        SourceTypeSymbol source => source.Definition,
        ConstructedTypeSymbol constructed when constructed.Definition is SourceTypeSymbol source => source.Definition,
        _ => null,
    };

    public static TypeSymbol Substitute(TypeSymbol type, TypeSymbol runtime)
    {
        if (type is TypeParameterSymbol parameter && runtime is ConstructedTypeSymbol constructed && constructed.Definition is SourceTypeSymbol source)
        {
            var index = source.Definition.TypeParameters.FindIndex(item => ReferenceEquals(item, parameter) || ReferenceEquals(item, parameter.Canonical) || item.Name == parameter.Name);
            if (index >= 0 && index < constructed.Arguments.Length)
            {
                var argument = constructed.Arguments[index];
                return type.IsNullable ? argument.WithNullable(true) : argument;
            }
        }

        return type;
    }

    private static UsValueKind KindOf(TypeSymbol type)
    {
        var name = type.WithNullable(false).Name;
        return name switch
        {
            "mantıksal" => UsValueKind.Bool,
            "karakter" => UsValueKind.Char,
            "tamsayı" => UsValueKind.Int,
            "utamsayı" => UsValueKind.UInt,
            "ondalık" => UsValueKind.Float64,
            "gondalık" => UsValueKind.Float32,
            "hondalık" => UsValueKind.Decimal,
            "metin" => UsValueKind.Text,
            _ => UsValueKind.Object,
        };
    }

    private static bool CanWiden(UsValueKind from, UsValueKind to) =>
        from == UsValueKind.Int && to is UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal
        || from == UsValueKind.UInt && to == UsValueKind.Float64;
}

public sealed partial class Evaluator
{
    private async ValueTask<UsValue> Create(BoundNode node)
    {
        DirectConstructions++;
        var instance = UsInstance.Allocate(node.Type);
        var arguments = node.Children.Where(child => !child.Flag).ToArray();
        var initializers = node.Children.Where(child => child.Flag).ToArray();
        await Initialize(instance, instance.Definition, node.Symbol as FunctionSymbol, arguments, initializers, node.Span, null);
        return UsValue.FromReference(instance);
    }

    private async ValueTask Initialize(UsInstance instance, SourceTypeSymbol type, FunctionSymbol? constructor, BoundNode[] arguments, BoundNode[] initializers, TextSpan span, UsValue[]? injected)
    {
        if (type.BaseType is SourceTypeSymbol baseType && baseType.Definition.TypeKind == SourceTypeKind.Class)
        {
            var parameterless = baseType.Definition.Methods.FirstOrDefault(method => method.IsConstructor && method.Parameters.All(parameter => parameter.HasDefault));
            await Initialize(instance, baseType.Definition, parameterless, [], [], span, null);
        }

        await ApplyDefaults(instance, type, span);
        if (constructor is not null)
        {
            if (injected is not null)
            {
                await InvokeValues(constructor, instance, injected, span);
            }
            else
            {
                await Invoke(constructor, null, false, instance, arguments, span);
            }
        }

        foreach (var initializer in initializers)
        {
            if (initializer.Symbol is not FieldSymbol field)
            {
                continue;
            }

            var value = initializer.Children.Length == 0 ? UsValue.Unit : await Evaluate(initializer.Children[0]);
            WriteField(UsValue.FromReference(instance), field, value, initializer.Span);
        }

        if (type.TypeKind == SourceTypeKind.Model)
        {
            foreach (var field in type.Fields)
            {
                if (instance.Assigned(field))
                {
                    continue;
                }

                if (field.IsKey)
                {
                    continue;
                }

                if (field.IsRequired || !field.Type.IsNullable && field.Default is null)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.MissingField, $"'{field.Name}' alanı verilmelidir.", span.Start, span.Length);
                }
            }
        }
    }

    private async ValueTask ApplyDefaults(UsInstance instance, SourceTypeSymbol type, TextSpan span)
    {
        foreach (var field in type.Fields)
        {
            if (field.Default is not null)
            {
                WriteField(UsValue.FromReference(instance), field, await Evaluate(field.Default), span);
                continue;
            }

            if (type.TypeKind == SourceTypeKind.Model)
            {
                if (field.IsKey)
                {
                    var keyZero = Zero(field.Type);
                    if (!keyZero.IsNull || field.Type.IsNullable)
                    {
                        WriteField(UsValue.FromReference(instance), field, keyZero, span);
                    }

                    continue;
                }

                if (field.Type.IsNullable)
                {
                    WriteField(UsValue.FromReference(instance), field, UsValue.Null, span);
                }

                continue;
            }

            var zero = Zero(field.Type);
            if (!zero.IsNull || field.Type.IsNullable)
            {
                WriteField(UsValue.FromReference(instance), field, zero, span);
            }
        }
    }

    private async ValueTask<UsValue> Copy(BoundNode node)
    {
        var source = RequireInstance(await Evaluate(node.Children[0]), node.Span);
        var copy = UsInstance.Allocate(node.Type);
        foreach (var field in UsInstance.Fields(source.Definition))
        {
            if (!source.Assigned(field))
            {
                continue;
            }

            copy.Write(field, source.Read(field, node.Span.Start, node.Span.Length), copy.RuntimeType, node.Span.Start, node.Span.Length);
        }

        foreach (var initializer in node.Children.Skip(1))
        {
            if (initializer.Symbol is not FieldSymbol field)
            {
                continue;
            }

            var value = initializer.Children.Length == 0 ? UsValue.Unit : await Evaluate(initializer.Children[0]);
            WriteField(UsValue.FromReference(copy), field, value, initializer.Span);
        }

        return UsValue.FromReference(copy);
    }

    private async ValueTask<UsValue> Member(BoundNode node)
    {
        var receiver = await Evaluate(node.Children[0]);
        if (node.Operator == TokenKind.QuestionDot && receiver.IsNull)
        {
            return UsValue.Null;
        }

        if (receiver.Kind == UsValueKind.Text && node.Symbol is null && node.Constant is null)
        {
            return UsValue.FromInt((receiver.Text ?? string.Empty).Length);
        }

        if (node.Constant is "adet" or "uzunluk" or "ilk" or "min" or "maks" or "toplam" or "ortalama")
        {
            return await CollectionProperty(node.Constant, receiver, node.Span);
        }

        if (node.Symbol is { Kind: SymbolKind.Variable } && node.Type is SourceTypeSymbol { TypeKind: SourceTypeKind.Option })
        {
            return UsValue.FromReference(node.Symbol);
        }

        if (node.Symbol is TableSymbol table)
        {
            if (_database is null)
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{table.Name}' tablosu yok.", node.Span.Start, node.Span.Length);
            }

            return UsValue.FromReference(new UsTableQuery(_database, table));
        }

        if (node.Symbol is FieldSymbol field)
        {
            return Field(receiver, field, node.Span);
        }

        if (node.Symbol is FunctionSymbol)
        {
            return UsValue.FromReference(node.Symbol);
        }

        if (node.Constant is "milisaniye" or "saniye" or "dakika" or "saat" or "gün" or "hafta")
        {
            return UsValue.FromReference(new UsDuration(Milliseconds(receiver, node.Constant, node.Span)));
        }

        throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Bu üye henüz çalıştırılamıyor.", node.Span.Start, node.Span.Length);
    }

    private async ValueTask<Signal> Select(BoundNode node)
    {
        var subject = await Evaluate(node.Children[0]);
        for (var i = 1; i + 1 < node.Children.Length; i += 2)
        {
            var pattern = node.Children[i];
            if (pattern.Constant == "_" || Same(subject, await Evaluate(pattern)))
            {
                return await Execute(node.Children[i + 1]);
            }
        }

        throw new UsRuntimeException(UsRuntimeCodes.SelectMiss, "seç kolu eşleşmedi.", node.Span.Start, node.Span.Length);
    }

    private async ValueTask<UsValue> Invoke(FunctionSymbol function, BoundNode node, bool baseCall) =>
        await Invoke(function, node, baseCall, null, null, node.Span);

    private async ValueTask<UsValue> Invoke(FunctionSymbol function, BoundNode? node, bool baseCall, UsInstance? existing, BoundNode[]? prepared, TextSpan span)
    {
        var template = function.Template ?? function;
        var body = template.BoundBody;
        var caller = _state.Functions.Count == 0 ? null : _state.Functions.Peek();
        if (template.Visibility == Visibility.Private && caller?.DeclaringType is not null && !ReferenceEquals(caller.DeclaringType.Definition, template.DeclaringType?.Definition))
        {
            throw new UsRuntimeException(UsRuntimeCodes.RuntimeVisibility, $"'{function.Name}' bu konumdan görünmez.", span.Start, span.Length);
        }

        UsInstance? receiver = existing;
        existing?.EnsureActive();
        var argumentStart = 0;
        if (existing is null && node is not null && template.DeclaringType is not null && !template.IsConstructor)
        {
            var firstIsReceiver = !template.IsStatic || node.Children.Length > 0 && node.Children[0].Symbol is TypeSymbol;
            if (firstIsReceiver)
            {
                argumentStart = 1;
                if (!template.IsStatic)
                {
                    var raw = await Evaluate(node.Children[0]);
                    if (raw.IsNull)
                    {
                        throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Boş örneğin üyesi çağrılamaz.", span.Start, span.Length);
                    }

                    receiver = RequireInstance(raw, node.Children[0].Span);
                    receiver.EnsureActive();
                    if (!baseCall)
                    {
                        function = Dispatch(template, receiver);
                        template = function.Template ?? function;
                        body = template.BoundBody;
                    }
                }
            }
        }

        if (receiver is not null)
        {
            _state.Receivers.Push(UsValue.FromReference(receiver));
        }

        try
        {
            var arguments = prepared ?? node?.Children.Skip(argumentStart).ToArray() ?? [];
            var frame = new Dictionary<Symbol, Cell>();
            for (var i = 0; i < template.Parameters.Count; i++)
            {
                var parameter = template.Parameters[i];
                if (i < arguments.Length)
                {
                    var argument = arguments[i];
                    if (parameter.IsRef || parameter.IsOut || argument.Operator is TokenKind.Ref or TokenKind.Cikti)
                    {
                        frame[parameter] = await Alias(argument).ConfigureAwait(false);
                        continue;
                    }

                    var cell = new Cell(parameter.Type.IsNullable);
                    cell.Write(Convert(await Evaluate(argument), parameter.Type, argument.Span), argument.Span.Start, argument.Span.Length);
                    frame[parameter] = cell;
                    continue;
                }

                var omitted = new Cell(parameter.Type.IsNullable);
                frame[parameter] = omitted;
                if (parameter.Default is not null)
                {
                    _state.Frames.Push(frame);
                    try
                    {
                        omitted.Write(Convert(await Evaluate(parameter.Default), parameter.Type, span), span.Start, span.Length);
                    }
                    finally
                    {
                        _state.Frames.Pop();
                    }
                }
            }

            _state.Frames.Push(frame);
            _state.Functions.Push(template);
            try
            {
                if (template.IsAbstract || body is null)
                {
                    throw new UsRuntimeException(template.IsAbstract ? UsRuntimeCodes.InterpreterInvariant : UsRuntimeCodes.NotExecutable, $"'{function.Name}' gövdesi yok.", span.Start, span.Length);
                }

                var signal = await Execute(body);
                foreach (var parameter in template.Parameters.Where(parameter => parameter.IsOut))
                {
                    if (!frame[parameter].IsAssigned)
                    {
                        throw new UsRuntimeException(UsRuntimeCodes.OutputRuntime, $"'{parameter.Name}' çıktı parametresi atanmalıdır.", span.Start, span.Length);
                    }
                }

                if (signal.Kind == SignalKind.Return)
                {
                    return template.ReturnType is UnitTypeSymbol ? UsValue.Unit : Convert(signal.Value, template.ReturnType, span);
                }

                return UsValue.Unit;
            }
            catch (UsRuntimeException exception)
            {
                exception.Frames.Add(template.Name);
                throw;
            }
            catch (UsThrown exception)
            {
                exception.Frames.Add(template.Name);
                throw;
            }
            finally
            {
                _state.Functions.Pop();
                _state.Frames.Pop();
            }
        }
        finally
        {
            if (receiver is not null)
            {
                _state.Receivers.Pop();
            }
        }
    }

    private static FunctionSymbol Dispatch(FunctionSymbol slot, UsInstance receiver)
    {
        var seen = new HashSet<SourceTypeSymbol>();
        for (var current = receiver.Definition; current is not null && seen.Add(current.Definition); current = current.BaseType as SourceTypeSymbol)
        {
            foreach (var method in current.Definition.Methods)
            {
                if (ReferenceEquals(method, slot) || method.Targets.Contains(slot))
                {
                    return method;
                }
            }
        }

        return slot;
    }

    private async ValueTask<Cell> Alias(BoundNode argument)
    {
        if (argument.Symbol is FieldSymbol field)
        {
            var receiver = argument.Kind == BoundKind.Member ? await Evaluate(argument.Children[0]) : Receiver(argument.Span);
            return CellOf(RequireInstance(receiver, argument.Span), field);
        }

        if (argument.Symbol is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "ref hedefi yok.", argument.Span.Start, argument.Span.Length);
        }

        return Find(argument.Symbol);
    }

    private static Cell CellOf(UsInstance instance, FieldSymbol field)
    {
        var cell = instance.GetCell(field);
        return cell;
    }

    private UsValue Field(UsValue receiver, FieldSymbol field, TextSpan span)
    {
        if (receiver.IsNull)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NullReceiver, "Boş örneğin üyesi okunamaz.", span.Start, span.Length);
        }

        return RequireInstance(receiver, span).Read(field, span.Start, span.Length);
    }

    private void WriteField(UsValue receiver, FieldSymbol field, UsValue value, TextSpan span)
    {
        var instance = RequireInstance(receiver, span);
        var expected = UsInstance.Substitute(field.Type, instance.RuntimeType);
        var stored = expected is PrimitiveTypeSymbol or SourceTypeSymbol ? Convert(value, expected, span) : value;
        instance.Write(field, stored, instance.RuntimeType, span.Start, span.Length);
    }

    private UsValue Receiver(TextSpan span)
    {
        if (_state.Receivers.Count == 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "bu örneği yok.", span.Start, span.Length);
        }

        return _state.Receivers.Peek();
    }

    private static UsInstance RequireInstance(UsValue value, TextSpan span)
    {
        if (value.Reference is UsInstance instance)
        {
            return instance;
        }

        throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Örnek gerekir.", span.Start, span.Length);
    }

    private static bool Same(UsValue left, UsValue right)
    {
        if (left.Kind == UsValueKind.Object || right.Kind == UsValueKind.Object)
        {
            return ReferenceEquals(left.Reference, right.Reference);
        }

        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Equals(right);
    }

    private static UsValue Zero(TypeSymbol type)
    {
        if (type.IsNullable)
        {
            return UsValue.Null;
        }

        var name = type.WithNullable(false).Name;
        return name switch
        {
            "mantıksal" => UsValue.FromBool(false),
            "karakter" => UsValue.FromChar('\0'),
            "tamsayı" => UsValue.FromInt(0),
            "utamsayı" => UsValue.FromUInt(0),
            "ondalık" => UsValue.FromFloat64(0),
            "gondalık" => UsValue.FromFloat32(0),
            "hondalık" => UsValue.FromDecimal(0),
            "metin" => UsValue.FromText(string.Empty),
            _ => UsValue.Null,
        };
    }
}
