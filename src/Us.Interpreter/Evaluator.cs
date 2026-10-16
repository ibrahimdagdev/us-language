using System.Globalization;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public enum SignalKind
{
    Next,
    Break,
    Continue,
    Return,
}

public readonly record struct Signal(SignalKind Kind, UsValue Value)
{
    public static Signal Next(UsValue value) => new(SignalKind.Next, value);

    public static Signal Break { get; } = new(SignalKind.Break, UsValue.Unit);

    public static Signal Continue { get; } = new(SignalKind.Continue, UsValue.Unit);

    public static Signal Return(UsValue value) => new(SignalKind.Return, value);
}

public sealed class Cell
{
    private UsValue _value;

    public Cell(bool nullable) => Nullable = nullable;

    public bool Nullable { get; }

    public bool IsAssigned { get; private set; }

    public UsValue Read(int start, int length)
    {
        if (!IsAssigned)
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnassignedRead, "Değişken okunmadan önce atanmalıdır.", start, length);
        }

        return _value;
    }

    public void Write(UsValue value, int start, int length)
    {
        value.EnsureCanStore(Nullable, start, length);
        _value = value;
        IsAssigned = true;
    }

    public void Capture(out UsValue value, out bool assigned)
    {
        value = _value;
        assigned = IsAssigned;
    }

    public void Restore(UsValue value, bool assigned)
    {
        _value = value;
        IsAssigned = assigned;
    }
}

public sealed partial class Evaluator
{
    private readonly IOutput _output;
    private readonly IUsScheduler _scheduler;
    private readonly UsHost _host;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<Diagnostic> _runtime = [];
    private ExecState _state = new();
    private SourceFile? _file;
    private string? _testName;
    private UsContainer? _container;
    private UsServiceScope? _scope;
    private MemoryDatabase? _database;
    private TxState _tx;
    private bool _sharedServices;

    public UsHost Host => _host;

    public UsContainer? Container => _container;

    internal UsServiceScope? Scope => _scope;

    public int DirectConstructions { get; private set; }

    public Evaluator(IOutput output, IUsScheduler? scheduler = null)
        : this(output, new UsHost { Scheduler = scheduler ?? RealScheduler.Instance })
    {
    }

    public Evaluator(IOutput output, UsHost host)
    {
        _output = output;
        _host = host;
        _scheduler = host.Scheduler;
        _state.Frames.Push(new Dictionary<Symbol, Cell>());
    }

    public int RootCellCount => _state.Frames.Count == 0 ? 0 : _state.Frames.Last().Count;

    public IReadOnlyList<Diagnostic> RuntimeDiagnostics => _runtime;

    public async ValueTask<UsValue> ExecuteAsync(BoundCompilation compilation, SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
        OpenServices(compilation);
        if (compilation.Members.IsDefaultOrEmpty)
        {
            CloseServices();
            return UsValue.Unit;
        }

        UsValue result = UsValue.Unit;
        await EnterAsync().ConfigureAwait(false);
        try
        {
            foreach (var member in compilation.Members)
            {
                var signal = await Execute(member).ConfigureAwait(false);
                if (member.Operator == TokenKind.Dogrula)
                {
                    result = signal.Value;
                }

                if (signal.Kind is SignalKind.Break or SignalKind.Continue or SignalKind.Return)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Üst düzeyde döngü sinyali kaldı.", member.Span.Start, member.Span.Length);
                }
            }
        }
        catch (UsRuntimeException exception)
        {
            _runtime.Add(ToDiagnostic(exception));
        }
        catch (UsThrown exception)
        {
            _runtime.Add(ToThrown(exception));
        }
        finally
        {
            CloseServices();
            Exit();
        }

        return result;
    }

    public async ValueTask RunTestAsync(BoundCompilation compilation, SourceFile file, TestSymbol test)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(test);
        _file = file;
        _testName = test.Name;
        OpenServices(compilation);
        await EnterAsync().ConfigureAwait(false);
        try
        {
            foreach (var member in compilation.Members)
            {
                if (member.Symbol is TestSymbol)
                {
                    continue;
                }

                var signal = await Execute(member).ConfigureAwait(false);
                if (signal.Kind is SignalKind.Break or SignalKind.Continue or SignalKind.Return)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Üst düzeyde döngü sinyali kaldı.", member.Span.Start, member.Span.Length);
                }
            }

            await Execute(test.Body).ConfigureAwait(false);
        }
        finally
        {
            CloseServices();
            Exit();
            _testName = null;
        }
    }

    internal async ValueTask<UsInstance> TakeAsync(BoundCompilation compilation, SourceFile file, string name)
    {
        _file = file;
        OpenServices(compilation);
        var registration = compilation.Services.Single(item => item.ServiceType.Name == name);
        var value = await _scope!.ResolveAsync(registration.ServiceType).ConfigureAwait(false);
        return (UsInstance)value.Reference!;
    }

    internal ValueTask CallAsync(UsInstance instance, string method)
    {
        var function = instance.Definition.Methods.First(item => item.Name == method && !item.IsConstructor);
        return InvokeValues(function, instance, [], default);
    }

    internal void CloseScope() => _scope?.Close();

    internal void AttachShared(UsContainer container, UsServiceScope scope, MemoryDatabase? database)
    {
        _container = container;
        _scope = scope;
        _database = database;
        _tx = TxState.None;
        _sharedServices = true;
    }

    internal ValueTask<UsValue> ConstructServicePublic(ServiceRegistration registration, UsServiceScope scope, List<SourceTypeSymbol> chain) =>
        ConstructService(registration, scope, chain);

    internal async ValueTask ExecutePublic(BoundNode node) => await Execute(node).ConfigureAwait(false);

    internal ValueTask<UsValue> ReadJsonTextPublic(string text, TypeSymbol type, TextSpan span) => ReadJsonText(text, type, span);

    internal string WriteJsonPublic(UsValue value, TextSpan span) => WriteJson(value, span);

    internal async ValueTask EnterPublicAsync() => await EnterAsync().ConfigureAwait(false);

    internal void ExitPublic() => Exit();

    internal async ValueTask EnsureSetupAssignedAsync(ControllerSymbol controller)
    {
        foreach (var setup in controller.Setup)
        {
            if (setup.Symbol is null)
            {
                continue;
            }

            var assigned = false;
            foreach (var frame in _state.Frames)
            {
                if (frame.TryGetValue(setup.Symbol, out var cell) && cell.IsAssigned)
                {
                    assigned = true;
                    break;
                }
            }

            if (assigned)
            {
                continue;
            }

            var created = new Cell(setup.Type.IsNullable);
            if (setup.Constant == "db")
            {
                if (_database is null)
                {
                    throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, "Veritabanı yok.", setup.Span.Start, setup.Span.Length);
                }

                created.Write(UsValue.FromReference(_database), setup.Span.Start, setup.Span.Length);
            }
            else if (setup.Constant == "servis" && setup.Type is SourceTypeSymbol serviceType && _scope is not null)
            {
                var resolved = await _scope.ResolveAsync(serviceType.Definition, setup.Span).ConfigureAwait(false);
                created.Write(resolved, setup.Span.Start, setup.Span.Length);
            }

            _state.Frames.Peek()[setup.Symbol] = created;
        }
    }

    internal async ValueTask<UsValue> InvokeHandlerAsync(FunctionSymbol function, UsValue[] values, TextSpan span)
    {
        var template = function.Template ?? function;
        var frame = new Dictionary<Symbol, Cell>();
        for (var index = 0; index < template.Parameters.Count; index++)
        {
            var parameter = template.Parameters[index];
            var cell = new Cell(parameter.Type.IsNullable);
            frame[parameter] = cell;
            if (index < values.Length)
            {
                cell.Write(Convert(values[index], parameter.Type, span), span.Start, span.Length);
            }
        }

        _state.Frames.Push(frame);
        _state.Functions.Push(template);
        try
        {
            var body = template.BoundBody ?? throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"'{function.Name}' gövdesi yok.", span.Start, span.Length);
            var signal = await Execute(body).ConfigureAwait(false);
            return signal.Kind == SignalKind.Return
                ? template.ReturnType is UnitTypeSymbol ? UsValue.Unit : Convert(signal.Value, template.ReturnType, span)
                : UsValue.Unit;
        }
        finally
        {
            _state.Functions.Pop();
            _state.Frames.Pop();
        }
    }

    private void OpenServices(BoundCompilation compilation)
    {
        if (_sharedServices)
        {
            return;
        }

        _container = new UsContainer(compilation.Services, ConstructService);
        _scope = _container.CreateScope();
        _database = compilation.Databases.IsDefaultOrEmpty ? null : new MemoryDatabase(compilation.Databases);
        _tx = TxState.None;
    }

    private void CloseServices()
    {
        if (_sharedServices)
        {
            return;
        }

        _scope?.Close();
    }

    private async ValueTask<UsValue> ConstructService(ServiceRegistration registration, UsServiceScope scope, List<SourceTypeSymbol> chain)
    {
        var type = registration.ServiceType;
        var instance = UsInstance.Allocate(type);
        UsValue[]? injected = null;
        if (registration.Constructor is { Parameters.Count: > 0 } constructor)
        {
            injected = new UsValue[constructor.Parameters.Count];
            for (var index = 0; index < constructor.Parameters.Count; index++)
            {
                var parameter = constructor.Parameters[index];
                var dependency = ServiceDependency(parameter.Type);
                if (dependency?.ServiceLifetime is not null)
                {
                    injected[index] = await scope.ResolveAsync(dependency, registration.Span, chain).ConfigureAwait(false);
                }
                else if (parameter.Default is not null)
                {
                    injected[index] = await Evaluate(parameter.Default).ConfigureAwait(false);
                }
                else
                {
                    throw new UsRuntimeException(UsRuntimeCodes.ServiceMissing, $"'{parameter.Type.DisplayName}' kayıtlı değil.", registration.Span.Start, registration.Span.Length);
                }
            }
        }

        await Initialize(instance, type, registration.Constructor, [], [], registration.Span, injected).ConfigureAwait(false);
        if (registration.Lifetime == ServiceLifetime.Scoped)
        {
            scope.Own(instance);
        }

        return UsValue.FromReference(instance);
    }

    private static SourceTypeSymbol? ServiceDependency(TypeSymbol type) => type switch
    {
        SourceTypeSymbol source => source.Definition,
        ConstructedTypeSymbol constructed when constructed.Definition is SourceTypeSymbol source => source.Definition,
        _ => null,
    };

    private async ValueTask<Signal> Execute(BoundNode node)
    {
        switch (node.Kind)
        {
            case BoundKind.Block when node.Operator == TokenKind.Eesislem:
                await Concurrent(node).ConfigureAwait(false);
                return Signal.Next(UsValue.Unit);
            case BoundKind.Block when node.Constant == "beklenenHata":
                return await ExpectedError(node).ConfigureAwait(false);
            case BoundKind.Block:
                return await Block(node).ConfigureAwait(false);
            case BoundKind.Local:
                return await Local(node).ConfigureAwait(false);
            case BoundKind.Expression when node.Operator == TokenKind.Firlat:
                await Throw(node).ConfigureAwait(false);
                return Signal.Next(UsValue.Unit);
            case BoundKind.Expression when node.Operator == TokenKind.Beklenen:
                await AssertExpected(node).ConfigureAwait(false);
                return Signal.Next(UsValue.Unit);
            case BoundKind.Expression:
                return await Expression(node).ConfigureAwait(false);
            case BoundKind.If:
                return await If(node).ConfigureAwait(false);
            case BoundKind.Loop:
                return await Loop(node).ConfigureAwait(false);
            case BoundKind.Jump:
                return Jump(node);
            case BoundKind.Result:
                return Signal.Return(node.Children.Length == 0 ? UsValue.Unit : await Evaluate(node.Children[0]).ConfigureAwait(false));
            case BoundKind.Type:
                return Signal.Next(UsValue.Unit);
            case BoundKind.Other when node.Operator == TokenKind.Dene:
                return await Try(node).ConfigureAwait(false);
            case BoundKind.Other when node.Operator == TokenKind.Sec:
                return await Select(node).ConfigureAwait(false);
            case BoundKind.Other when node.Operator == TokenKind.Dogrula:
                return Signal.Next(await Validate(node).ConfigureAwait(false));
            case BoundKind.Other when node.Constant == "ayar":
                await BindSetting(node).ConfigureAwait(false);
                return Signal.Next(UsValue.Unit);
            case BoundKind.Other when node.Operator is TokenKind.Onayla or TokenKind.Gerial:
                ExitTransaction(node);
                return Signal.Next(UsValue.Unit);
            case BoundKind.Other when node.Operator is null && node.Children.Length == 0:
                return Signal.Next(UsValue.Unit);
            default:
                return Signal.Next(await Evaluate(node).ConfigureAwait(false));
        }
    }

    private async ValueTask<UsValue> Evaluate(BoundNode node)
    {
        switch (node.Kind)
        {
            case BoundKind.Literal:
                return Literal(node);
            case BoundKind.Name:
                return Read(node);
            case BoundKind.Unary when node.Operator == TokenKind.Bekle:
                return await Await(node).ConfigureAwait(false);
            case BoundKind.Unary:
                return await Unary(node).ConfigureAwait(false);
            case BoundKind.Binary:
                return await Binary(node).ConfigureAwait(false);
            case BoundKind.Assignment:
                return await Assignment(node).ConfigureAwait(false);
            case BoundKind.Call when node.Constant == "tetikle":
                return await Trigger(node).ConfigureAwait(false);
            case BoundKind.Call:
                return await Call(node).ConfigureAwait(false);
            case BoundKind.Member:
                return await Member(node).ConfigureAwait(false);
            case BoundKind.Other when node.Operator == TokenKind.OpenBracket:
                return node.Flag ? await Index(node).ConfigureAwait(false) : await CollectionLiteral(node).ConfigureAwait(false);
            case BoundKind.If when node.Operator == TokenKind.EqualsGreater:
                return await ExpressionIf(node).ConfigureAwait(false);
            case BoundKind.Other when node.Operator == TokenKind.InterpolationStart:
                return await Interpolation(node).ConfigureAwait(false);
            case BoundKind.Other when node.Operator == TokenKind.EqualsGreater && node.Symbol is FunctionSymbol:
                return UsValue.FromReference(node.Symbol);
            case BoundKind.Other when node.Operator == TokenKind.Ile:
                return await Copy(node).ConfigureAwait(false);
            case BoundKind.Error:
                throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Hatalı düğüm çalıştırılamaz.", node.Span.Start, node.Span.Length);
            default:
                throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Bu yapı henüz çalıştırılamıyor.", node.Span.Start, node.Span.Length);
        }
    }

    private async ValueTask<Signal> Block(BoundNode node)
    {
        var transaction = node.Operator == TokenKind.Islem;
        if (transaction)
        {
            _database?.Begin();
            _tx = TxState.Active;
        }

        _state.Frames.Push(new Dictionary<Symbol, Cell>());
        try
        {
            foreach (var child in node.Children)
            {
                var signal = await Execute(child);
                if (signal.Kind != SignalKind.Next)
                {
                    if (transaction)
                    {
                        FinishTransaction(true);
                    }

                    return signal;
                }
            }

            if (transaction)
            {
                FinishTransaction(true);
            }

            return Signal.Next(UsValue.Unit);
        }
        catch
        {
            if (transaction)
            {
                FinishTransaction(false);
            }

            throw;
        }
        finally
        {
            _state.Frames.Pop();
        }
    }

    private async ValueTask<Signal> Local(BoundNode node)
    {
        if (node.Symbol is null)
        {
            foreach (var child in node.Children)
            {
                var signal = await Execute(child);
                if (signal.Kind != SignalKind.Next)
                {
                    return signal;
                }
            }

            return Signal.Next(UsValue.Unit);
        }

        var cell = new Cell(node.Type.IsNullable);
        _state.Frames.Peek()[node.Symbol] = cell;
        if (node.Constant == "db")
        {
            if (_database is null)
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, "Veritabanı yok.", node.Span.Start, node.Span.Length);
            }

            cell.Write(UsValue.FromReference(_database), node.Span.Start, node.Span.Length);
            return Signal.Next(UsValue.Unit);
        }

        if (node.Constant == "servis")
        {
            if (node.Type is not SourceTypeSymbol serviceType || _scope is null)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ServiceMissing, "Servis çözülemedi.", node.Span.Start, node.Span.Length);
            }

            var resolved = await _scope.ResolveAsync(serviceType.Definition, node.Span).ConfigureAwait(false);
            cell.Write(resolved, node.Span.Start, node.Span.Length);
            return Signal.Next(UsValue.Unit);
        }

        if (node.Children.Length > 0)
        {
            var raw = await Evaluate(node.Children[0]);
            var value = node.Symbol is VariableSymbol { Callable: not null } ? raw : Convert(raw, node.Type, node.Children[0].Span);
            cell.Write(value, node.Span.Start, node.Span.Length);
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<Signal> Expression(BoundNode node)
    {
        if (node.Children.Length > 0)
        {
            await Evaluate(node.Children[0]);
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<Signal> If(BoundNode node)
    {
        if (node.Operator == TokenKind.EqualsGreater)
        {
            await Evaluate(node);
            return Signal.Next(UsValue.Unit);
        }

        if (await IsTrue(node.Children[0]))
        {
            return await Execute(node.Children[1]);
        }

        var index = 2;
        while (index + 1 < node.Children.Length)
        {
            if (await IsTrue(node.Children[index]))
            {
                return await Execute(node.Children[index + 1]);
            }

            index += 2;
        }

        if (index < node.Children.Length)
        {
            return await Execute(node.Children[index]);
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<UsValue> ExpressionIf(BoundNode node)
    {
        var chosen = await IsTrue(node.Children[0]) ? node.Children[1] : node.Children[2];
        return await Evaluate(chosen);
    }

    private async ValueTask<Signal> Loop(BoundNode node)
    {
        if (node.Operator is null && node.Children.Length == 1)
        {
            return await Execute(node.Children[0]);
        }

        switch (node.Operator)
        {
            case TokenKind.Tekrarla:
                return await While(node).ConfigureAwait(false);
            case TokenKind.Yap:
                return await Do(node).ConfigureAwait(false);
            case TokenKind.Sonsuzdon:
                return await Forever(node).ConfigureAwait(false);
            case TokenKind.Don when node.Symbol is not null && node.Children.Length >= 3:
                return await Range(node).ConfigureAwait(false);
            case null when node.Symbol is not null && node.Children.Length == 2:
                return await ForEach(node).ConfigureAwait(false);
            default:
                throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Bu döngü henüz çalıştırılamıyor.", node.Span.Start, node.Span.Length);
        }
    }

    private async ValueTask<Signal> While(BoundNode node)
    {
        while (await IsTrue(node.Children[0]))
        {
            var signal = await Execute(node.Children[1]);
            if (signal.Kind == SignalKind.Break)
            {
                break;
            }

            if (signal.Kind == SignalKind.Return)
            {
                return signal;
            }

            if (signal.Kind == SignalKind.Continue)
            {
                continue;
            }
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<Signal> Do(BoundNode node)
    {
        while (true)
        {
            var signal = await Execute(node.Children[0]);
            if (signal.Kind == SignalKind.Break)
            {
                break;
            }

            if (signal.Kind == SignalKind.Return)
            {
                return signal;
            }

            if (signal.Kind == SignalKind.Continue)
            {
                if (!await IsTrue(node.Children[1]))
                {
                    break;
                }

                continue;
            }

            if (!await IsTrue(node.Children[1]))
            {
                break;
            }
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<Signal> Forever(BoundNode node)
    {
        while (true)
        {
            var signal = await Execute(node.Children[0]);
            if (signal.Kind == SignalKind.Break)
            {
                break;
            }

            if (signal.Kind == SignalKind.Return)
            {
                return signal;
            }
        }

        return Signal.Next(UsValue.Unit);
    }

    private async ValueTask<Signal> Range(BoundNode node)
    {
        var start = RequireInt(await Evaluate(node.Children[0]), node.Children[0].Span);
        var end = RequireInt(await Evaluate(node.Children[1]), node.Children[1].Span);
        var hasStep = node.Children.Length == 4;
        var stepNode = hasStep ? node.Children[2] : null;
        var step = stepNode is null ? 1 : RequireInt(await Evaluate(stepNode), stepNode.Span);
        if (step <= 0)
        {
            var span = stepNode?.Span ?? node.Span;
            throw new UsRuntimeException(UsRuntimeCodes.ZeroStep, "Adım sıfırdan büyük olmalıdır.", span.Start, span.Length);
        }

        var body = node.Children[^1];
        var direction = start == end ? 0 : start < end ? 1 : -1;
        var signed = direction * step;
        var current = start;
        _state.Frames.Push(new Dictionary<Symbol, Cell>());
        try
        {
            var cell = new Cell(false);
            while (true)
            {
                cell = new Cell(false);
                _state.Frames.Peek()[node.Symbol!] = cell;
                cell.Write(UsValue.FromInt(current), node.Span.Start, node.Span.Length);
                var signal = await Execute(body);
                if (signal.Kind == SignalKind.Break)
                {
                    break;
                }

                if (signal.Kind == SignalKind.Return)
                {
                    return signal;
                }

                if (direction == 0)
                {
                    break;
                }

                var next = (long)current + signed;
                if (direction > 0 && next > end || direction < 0 && next < end)
                {
                    break;
                }

                current = (int)next;
            }
        }
        finally
        {
            _state.Frames.Pop();
        }

        return Signal.Next(UsValue.Unit);
    }

    private Signal Jump(BoundNode node) => node.Operator switch
    {
        TokenKind.Cik => Signal.Break,
        TokenKind.Devam => Signal.Continue,
        _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Atlama hedefi yok.", node.Span.Start, node.Span.Length),
    };

    private UsValue Literal(BoundNode node)
    {
        if (node.Constant == "~")
        {
            return UsValue.Null;
        }

        var text = node.Constant ?? string.Empty;
        return KindOf(node.Type) switch
        {
            UsValueKind.Int => UsValue.FromInt(int.Parse(text, CultureInfo.InvariantCulture)),
            UsValueKind.UInt => UsValue.FromUInt(uint.Parse(text, CultureInfo.InvariantCulture)),
            UsValueKind.Float64 => UsValue.FromFloat64(double.Parse(text, CultureInfo.InvariantCulture)),
            UsValueKind.Float32 => UsValue.FromFloat32(float.Parse(text, CultureInfo.InvariantCulture)),
            UsValueKind.Decimal => UsValue.FromDecimal(decimal.Parse(text, CultureInfo.InvariantCulture)),
            UsValueKind.Bool => UsValue.FromBool(text == "doğru"),
            UsValueKind.Char => UsValue.FromChar(text.Length == 0 ? '\0' : text[0]),
            UsValueKind.Text => UsValue.FromText(text),
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Literal tipi yok.", node.Span.Start, node.Span.Length),
        };
    }

    private UsValue Read(BoundNode node)
    {
        if (node.Operator == TokenKind.Caret)
        {
            if (_state.Items.Count == 0)
            {
                throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "^ burada yok.", node.Span.Start, node.Span.Length);
            }

            return _state.Items.Peek();
        }

        if (node.Operator is TokenKind.Bu or TokenKind.Temel)
        {
            return Receiver(node.Span);
        }

        if (node.Symbol is TypeSymbol type)
        {
            return UsValue.FromReference(type);
        }

        if (node.Symbol is FieldSymbol field)
        {
            return Field(Receiver(node.Span), field, node.Span);
        }

        if (node.Symbol is ModuleSymbol)
        {
            return UsValue.FromReference(node.Symbol);
        }

        if (node.Symbol is VariableSymbol clock && clock.Name == "bugün")
        {
            return UsValue.FromReference(new UsDate(DateOnly.FromDateTime(_host.Clock.WallNow)));
        }

        if (node.Symbol is VariableSymbol now && now.Name == "şimdi")
        {
            return UsValue.FromReference(new UsDateTime(_host.Clock.WallNow));
        }

        if (node.Symbol is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Adın sembolü yok.", node.Span.Start, node.Span.Length);
        }

        return Find(node.Symbol).Read(node.Span.Start, node.Span.Length);
    }

    private async ValueTask<UsValue> Unary(BoundNode node)
    {
        var operand = node.Children[0];
        if (node.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            var current = RequireNumber(await Load(operand), operand.Span);
            var updated = node.Operator == TokenKind.PlusPlus
                ? UsMath.Add(current, UsValue.FromInt(1), current.Kind, node.Span.Start, node.Span.Length)
                : UsMath.Subtract(current, UsValue.FromInt(1), current.Kind, node.Span.Start, node.Span.Length);
            await Store(operand, updated, node.Span);
            return node.Flag ? current : updated;
        }

        var value = await Evaluate(operand);
        return node.Operator switch
        {
            TokenKind.Bang => UsValue.FromBool(!RequireBool(value, operand.Span)),
            TokenKind.Minus => UsMath.Negate(value, node.Span.Start, node.Span.Length),
            TokenKind.Plus => value,
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Tekil işleç yok.", node.Span.Start, node.Span.Length),
        };
    }

    private async ValueTask<UsValue> Binary(BoundNode node)
    {
        var leftNode = node.Children[0];
        var rightNode = node.Children[1];
        if (node.Operator == TokenKind.AmpersandAmpersand)
        {
            return UsValue.FromBool(await IsTrue(leftNode) && await IsTrue(rightNode));
        }

        if (node.Operator == TokenKind.PipePipe)
        {
            return UsValue.FromBool(await IsTrue(leftNode) || await IsTrue(rightNode));
        }

        var left = await Evaluate(leftNode);
        if (node.Operator == TokenKind.QuestionQuestion)
        {
            return left.IsNull ? await Evaluate(rightNode) : Convert(left, node.Type, leftNode.Span);
        }

        var right = await Evaluate(rightNode);
        var op = node.Operator;
        if (TryTemporal(left, right, op, node.Span, out var temporal))
        {
            return temporal;
        }
        if (op is TokenKind.EqualEqual or TokenKind.BangEqual && (left.Kind is UsValueKind.Object or UsValueKind.Null || right.Kind is UsValueKind.Object or UsValueKind.Null))
        {
            var same = SameValue(left, right);
            return UsValue.FromBool(op == TokenKind.EqualEqual ? same : !same);
        }

        if (op is TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            var compared = UsMath.Compare(left, right, OperatorText(op.Value), node.Span.Start, node.Span.Length);
            return UsValue.FromBool(compared);
        }

        var target = KindOf(node.Type);
        return op switch
        {
            TokenKind.Plus => UsMath.Add(left, right, target, node.Span.Start, node.Span.Length),
            TokenKind.Minus => UsMath.Subtract(left, right, target, node.Span.Start, node.Span.Length),
            TokenKind.Star => UsMath.Multiply(left, right, target, node.Span.Start, node.Span.Length),
            TokenKind.Slash => UsMath.Divide(left, right, target, node.Span.Start, node.Span.Length),
            TokenKind.Percent => UsMath.Remainder(left, right, target, node.Span.Start, node.Span.Length),
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "İkili işleç yok.", node.Span.Start, node.Span.Length),
        };
    }

    private async ValueTask<UsValue> Assignment(BoundNode node)
    {
        var left = node.Children[0];
        if (left.Symbol is EventSymbol subscribed && node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals)
        {
            return await EditSubscription(subscribed, left, node.Children[1], node.Operator == TokenKind.PlusEquals, node.Span).ConfigureAwait(false);
        }

        var right = await Evaluate(node.Children[1]);
        if (left.Flag && left.Operator == TokenKind.OpenBracket)
        {
            var current = node.Operator == TokenKind.Equals ? default : await Index(left);
            var indexed = node.Operator switch
            {
                TokenKind.Equals => Convert(right, left.Type, node.Children[1].Span),
                TokenKind.PlusEquals => UsMath.Add(current, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
                TokenKind.MinusEquals => UsMath.Subtract(current, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
                TokenKind.StarEquals => UsMath.Multiply(current, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
                TokenKind.SlashEquals => UsMath.Divide(current, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
                _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Atama işleci yok.", node.Span.Start, node.Span.Length),
            };
            await AssignIndex(left, indexed);
            return indexed;
        }

        var currentCell = node.Operator == TokenKind.Equals ? default : await Load(left);
        UsValue value = node.Operator switch
        {
            TokenKind.Equals => Convert(right, left.Type, node.Children[1].Span),
            TokenKind.PlusEquals => UsMath.Add(currentCell, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
            TokenKind.MinusEquals => UsMath.Subtract(currentCell, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
            TokenKind.StarEquals => UsMath.Multiply(currentCell, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
            TokenKind.SlashEquals => UsMath.Divide(currentCell, right, KindOf(left.Type), node.Span.Start, node.Span.Length),
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Atama işleci yok.", node.Span.Start, node.Span.Length),
        };
        await Store(left, value, node.Span);
        return value;
    }

    private async ValueTask<UsValue> Load(BoundNode node) => node.Kind switch
    {
        BoundKind.Name => Read(node),
        BoundKind.Member => await Member(node).ConfigureAwait(false),
        _ => await Evaluate(node).ConfigureAwait(false),
    };

    private async ValueTask Store(BoundNode target, UsValue value, TextSpan span)
    {
        if (target.Symbol is FieldSymbol field)
        {
            var receiver = target.Kind == BoundKind.Member ? await Evaluate(target.Children[0]) : Receiver(span);
            WriteField(receiver, field, value, span);
            return;
        }

        if (target.Symbol is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Atama hedefi hücre değil.", span.Start, span.Length);
        }

        Find(target.Symbol).Write(value, span.Start, span.Length);
    }

    private async ValueTask<UsValue> Call(BoundNode node)
    {
        if (node.Operator == TokenKind.At)
        {
            return await Create(node);
        }

        if (node.Symbol is MappingSymbol mapping)
        {
            return await ConvertWith(mapping, node);
        }

        if (node.Symbol is null && node.Constant is not null)
        {
            if (node.Children.Length > 0 && node.Children[0].Type is TableQueryTypeSymbol)
            {
                return await TableCall(node).ConfigureAwait(false);
            }

            return await Query(node.Constant, node);
        }

        if (node.Symbol is FunctionSymbol intrinsic && intrinsic.Intrinsic != Intrinsic.None)
        {
            return await RunIntrinsic(intrinsic, node).ConfigureAwait(false);
        }

        if (node.Symbol is FunctionSymbol function && function.DeclaringType is null && function.Template is null && function.Name is "syaz" or "yaz")
        {
            var text = node.Children.Length == 0 ? string.Empty : (await Evaluate(node.Children[0]).ConfigureAwait(false)).ToDisplay();
            if (function.Name == "syaz")
            {
                _output.WriteLine(text);
            }
            else
            {
                _output.Write(text);
            }

            return UsValue.Unit;
        }

        if (node.Symbol is not FunctionSymbol target)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Bu çağrı henüz çalıştırılamıyor.", node.Span.Start, node.Span.Length);
        }

        var operation = Invoke(target, node, baseCall: node.Operator == TokenKind.Temel);
        if (target.IsAsync || target.Template?.IsAsync == true)
        {
            return UsValue.FromReference(new UsOperation(operation.AsTask()));
        }

        return await operation.ConfigureAwait(false);
    }

    private async ValueTask<UsValue> Interpolation(BoundNode node)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var child in node.Children)
        {
            builder.Append((await Evaluate(child).ConfigureAwait(false)).ToDisplay());
        }

        return UsValue.FromText(builder.ToString());
    }

    private async ValueTask<bool> IsTrue(BoundNode node)
    {
        var value = await Evaluate(node);
        if (value.Kind != UsValueKind.Bool)
        {
            throw new UsRuntimeException(UsRuntimeCodes.RuntimeCondition, "Koşul mantıksal olmalıdır.", node.Span.Start, node.Span.Length);
        }

        return value.Bool;
    }

    private Cell Find(Symbol symbol)
    {
        foreach (var frame in _state.Frames)
        {
            if (frame.TryGetValue(symbol, out var cell))
            {
                return cell;
            }
        }

        throw new UsRuntimeException(UsRuntimeCodes.UnassignedRead, $"'{symbol.Name}' hücresi yok.", 0, 0);
    }

    private static UsValue Convert(UsValue value, TypeSymbol type, TextSpan span)
    {
        if (value.IsNull)
        {
            return value;
        }

        if (type is PrimitiveTypeSymbol primitive && primitive.Canonical.Name is "tarih" or "saat" or "tarihsaat" or "süre" or "nesne" or "dinamik")
        {
            return value;
        }

        if (type is PrimitiveTypeSymbol)
        {
            return UsMath.Convert(value, KindOf(type), span.Start, span.Length);
        }

        return value;
    }

    private static UsValue RequireNumber(UsValue value, TextSpan span)
    {
        if (value.Kind is not (UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal))
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Sayısal değer gerekir.", span.Start, span.Length);
        }

        return value;
    }

    private static int RequireInt(UsValue value, TextSpan span)
    {
        if (value.Kind != UsValueKind.Int)
        {
            throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, "Aralık tamsayı ister.", span.Start, span.Length);
        }

        return value.Int;
    }

    private static bool RequireBool(UsValue value, TextSpan span)
    {
        if (value.Kind != UsValueKind.Bool)
        {
            throw new UsRuntimeException(UsRuntimeCodes.RuntimeCondition, "Koşul mantıksal olmalıdır.", span.Start, span.Length);
        }

        return value.Bool;
    }

    private static UsValueKind KindOf(TypeSymbol type)
    {
        var name = type.WithNullable(false).Name;
        return name switch
        {
            "tamsayı" => UsValueKind.Int,
            "utamsayı" => UsValueKind.UInt,
            "ondalık" => UsValueKind.Float64,
            "gondalık" => UsValueKind.Float32,
            "hondalık" => UsValueKind.Decimal,
            "mantıksal" => UsValueKind.Bool,
            "karakter" => UsValueKind.Char,
            "metin" => UsValueKind.Text,
            "tarih" or "saat" or "tarihsaat" or "süre" => UsValueKind.Object,
            _ => throw new UsRuntimeException(UsRuntimeCodes.InterpreterInvariant, $"'{name}' çalışma zamanı tipi yok.", 0, 0),
        };
    }

    private static string OperatorText(TokenKind kind) => kind switch
    {
        TokenKind.EqualEqual => "==",
        TokenKind.BangEqual => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        _ => string.Empty,
    };

    private async ValueTask AssertExpected(BoundNode node)
    {
        var condition = node.Children[0];
        if (condition.Kind == BoundKind.Binary && condition.Operator == TokenKind.EqualEqual && condition.Children.Length == 2)
        {
            var left = await Evaluate(condition.Children[0]).ConfigureAwait(false);
            var right = await Evaluate(condition.Children[1]).ConfigureAwait(false);
            if (!AreEqual(left, right, condition.Span))
            {
                throw AssertionFailure(node, $"Beklenen:\n{right.ToDisplay()}\nGerçek:\n{left.ToDisplay()}");
            }

            return;
        }

        var value = await Evaluate(condition).ConfigureAwait(false);
        if (!RequireBool(value, condition.Span))
        {
            throw AssertionFailure(node, null);
        }
    }

    private bool AreEqual(UsValue left, UsValue right, TextSpan span)
    {
        if (left.Kind is UsValueKind.Object or UsValueKind.Null || right.Kind is UsValueKind.Object or UsValueKind.Null)
        {
            return SameValue(left, right);
        }

        return UsMath.Compare(left, right, "==", span.Start, span.Length);
    }

    private UsTestFailure AssertionFailure(BoundNode node, string? detail)
    {
        var message = $"'{_testName}' testi başarısız oldu.\n{SourceSlice(node.Span).Trim()}";
        if (detail is not null)
        {
            message += "\n" + detail;
        }

        return new UsTestFailure(TestFailureKind.AssertionFailed, DiagnosticCodes.AssertionFailed, message, node.Span.Start, node.Span.Length);
    }

    private async ValueTask<Signal> ExpectedError(BoundNode node)
    {
        try
        {
            await Block(node).ConfigureAwait(false);
        }
        catch (UsThrown)
        {
            return Signal.Next(UsValue.Unit);
        }
        catch (UsRuntimeException)
        {
            return Signal.Next(UsValue.Unit);
        }

        throw new UsTestFailure(
            TestFailureKind.ExpectedErrorMissing,
            DiagnosticCodes.ExpectedErrorMissing,
            $"'{_testName}' testi hata beklemişti ama hata oluşmadı.",
            node.Span.Start,
            node.Span.Length);
    }

    private string SourceSlice(TextSpan span)
    {
        if (_file is null || span.Start < 0 || span.End > _file.Text.Length)
        {
            return string.Empty;
        }

        return _file.Text.ToString(span);
    }

    private Diagnostic ToDiagnostic(UsRuntimeException exception)
    {
        var file = _file!;
        var start = Math.Clamp(exception.Start, 0, file.Text.Length);
        var (line, column) = file.Lines.GetLineAndColumn(start);
        var message = exception.Frames.Count == 0
            ? exception.Message
            : exception.Message + " [" + string.Join(" → ", exception.Frames.AsEnumerable().Reverse()) + "]";
        return new Diagnostic(exception.Code, DiagnosticSeverity.Error, message, file.Path, new TextSpan(start, exception.Length), line, column, null);
    }

    private Diagnostic ToThrown(UsThrown exception)
    {
        var file = _file!;
        var start = Math.Clamp(exception.Start, 0, file.Text.Length);
        var (line, column) = file.Lines.GetLineAndColumn(start);
        var message = exception.Frames.Count == 0
            ? exception.Message
            : exception.Message + " [" + string.Join(" → ", exception.Frames.AsEnumerable().Reverse()) + "]";
        return new Diagnostic(UsRuntimeCodes.UserThrown, DiagnosticSeverity.Error, message, file.Path, new TextSpan(start, exception.Length), line, column, null);
    }
}
