using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public sealed class UsHandler
{
    public UsHandler(FunctionSymbol function, UsInstance? receiver)
    {
        Function = function;
        Receiver = receiver;
    }

    public FunctionSymbol Function { get; }

    public UsInstance? Receiver { get; }

    public bool Same(UsHandler other) =>
        ReferenceEquals(Function, other.Function) && ReferenceEquals(Receiver, other.Receiver);
}

public sealed class UsOperation
{
    public UsOperation(Task<UsValue> task) => Task = task;

    public Task<UsValue> Task { get; }
}

public sealed class ExecState
{
    public Stack<Dictionary<Symbol, Cell>> Frames { get; } = new();

    public Stack<UsValue> Items { get; } = new();

    public Stack<UsValue> Receivers { get; } = new();

    public Stack<FunctionSymbol> Functions { get; } = new();

    public bool OwnsGate { get; set; }

    public ExecState Fork()
    {
        var copy = new ExecState();
        foreach (var frame in Frames.Reverse())
        {
            copy.Frames.Push(frame);
        }

        foreach (var item in Items.Reverse())
        {
            copy.Items.Push(item);
        }

        foreach (var receiver in Receivers.Reverse())
        {
            copy.Receivers.Push(receiver);
        }

        foreach (var function in Functions.Reverse())
        {
            copy.Functions.Push(function);
        }

        return copy;
    }
}

public sealed class RealScheduler : IUsScheduler
{
    public static RealScheduler Instance { get; } = new();

    public ValueTask DelayAsync(long milliseconds)
    {
        if (milliseconds > int.MaxValue)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre bekleyemeyecek kadar büyük.", 0, 0);
        }

        return new ValueTask(Task.Delay((int)milliseconds));
    }
}

public sealed partial class Evaluator
{
    private async ValueTask EnterAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _state.OwnsGate = true;
    }

    private void Exit()
    {
        if (!_state.OwnsGate)
        {
            return;
        }

        _state.OwnsGate = false;
        _gate.Release();
    }

    private async ValueTask<UsValue> Wait(Task<UsValue> task, ExecState mine)
    {
        if (task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        Exit();
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            mine.OwnsGate = true;
            _state = mine;
        }
    }

    private async ValueTask<UsValue> Await(BoundNode node)
    {
        var value = await Evaluate(node.Children[0]).ConfigureAwait(false);
        if (value.Reference is UsDuration duration)
        {
            return await Wait(Delay(duration), _state).ConfigureAwait(false);
        }

        if (value.Reference is UsOperation operation)
        {
            if (operation.Task.IsCompleted || !_state.OwnsGate)
            {
                return await operation.Task.ConfigureAwait(false);
            }

            return await Wait(operation.Task, _state).ConfigureAwait(false);
        }

        return value;
    }

    private async Task<UsValue> Delay(UsDuration duration)
    {
        await _scheduler.DelayAsync(duration.Milliseconds).ConfigureAwait(false);
        return UsValue.Unit;
    }

    private async ValueTask Throw(BoundNode node)
    {
        var value = await Evaluate(node.Children[0]).ConfigureAwait(false);
        throw new UsThrown(value, ErrorMessage(value), node.Span.Start, node.Span.Length);
    }

    private async ValueTask<Signal> Try(BoundNode node)
    {
        var preparation = node.Children[0];
        var body = node.Children[1];
        var finalizer = node.Children[2];
        var catches = node.Children[3];
        Exception? pending = null;
        var signal = Signal.Next(UsValue.Unit);
        var skipBody = false;
        try
        {
            if (preparation.Operator is TokenKind.Basinda or TokenKind.BasindaUnderscore)
            {
                try
                {
                    await Execute(preparation).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
                {
                    skipBody = true;
                    if (preparation.Operator == TokenKind.BasindaUnderscore)
                    {
                        pending = exception;
                    }
                    else if (!await Catch(catches, exception).ConfigureAwait(false))
                    {
                        pending = exception;
                    }
                }
            }

            if (!skipBody)
            {
                try
                {
                    signal = await Execute(body).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
                {
                    if (!await Catch(catches, exception).ConfigureAwait(false))
                    {
                        pending = exception;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
        {
            pending = exception;
        }

        Exception? cleanup = null;
        try
        {
            await Execute(finalizer).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
        {
            cleanup = exception;
        }

        if (cleanup is not null)
        {
            throw cleanup;
        }

        if (pending is not null)
        {
            throw pending;
        }

        return signal;
    }

    private async ValueTask<bool> Catch(BoundNode catches, Exception exception)
    {
        foreach (var clause in catches.Children)
        {
            if (!Matches(clause, exception))
            {
                continue;
            }

            var value = exception is UsThrown thrown ? thrown.Error : ErrorObject(clause.Type, exception.Message);
            var cell = new Cell(clause.Type.IsNullable);
            cell.Write(value, clause.Span.Start, clause.Span.Length);
            _state.Frames.Peek()[clause.Symbol!] = cell;
            await Execute(clause.Children[0]).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static bool Matches(BoundNode clause, Exception exception)
    {
        if (clause.Flag)
        {
            return true;
        }

        return exception is UsThrown thrown
            && thrown.Error.Reference is UsInstance instance
            && Derives(instance.Definition, clause.Type);
    }

    private static bool Derives(SourceTypeSymbol instance, TypeSymbol catchType)
    {
        var target = catchType as SourceTypeSymbol ?? (catchType as ConstructedTypeSymbol)?.Definition as SourceTypeSymbol;
        if (target is null)
        {
            return false;
        }

        var seen = new HashSet<SourceTypeSymbol>();
        for (var current = instance.Definition; current is not null && seen.Add(current); current = current.BaseType as SourceTypeSymbol)
        {
            if (ReferenceEquals(current.Definition, target.Definition))
            {
                return true;
            }
        }

        return false;
    }

    private static UsValue ErrorObject(TypeSymbol type, string message)
    {
        var instance = UsInstance.Allocate(type);
        var field = UsInstance.Fields(instance.Definition).First(item => item.Name == "mesaj");
        instance.Write(field, UsValue.FromText(message), instance.RuntimeType, 0, 0);
        return UsValue.FromReference(instance);
    }

    private static string ErrorMessage(UsValue value)
    {
        if (value.Reference is UsInstance instance)
        {
            var field = UsInstance.Fields(instance.Definition).FirstOrDefault(item => item.Name == "mesaj");
            if (field is not null && instance.Assigned(field))
            {
                return instance.Read(field, 0, 0).ToDisplay();
            }
        }

        return "Hata";
    }

    private static long Milliseconds(UsValue receiver, string unit, TextSpan span)
    {
        long magnitude = receiver.Kind switch
        {
            UsValueKind.Int => receiver.Int,
            UsValueKind.UInt => receiver.UInt,
            UsValueKind.Float64 => checked((long)receiver.Float64),
            UsValueKind.Float32 => checked((long)receiver.Float32),
            UsValueKind.Decimal => checked((long)receiver.Decimal),
            _ => throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre sayı ister.", span.Start, span.Length),
        };
        long scale = unit switch
        {
            "milisaniye" => 1,
            "saniye" => 1_000,
            "dakika" => 60_000,
            "saat" => 3_600_000,
            "gün" => 86_400_000,
            "hafta" => 604_800_000,
            _ => 1,
        };
        try
        {
            return checked(magnitude * scale);
        }
        catch (OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", span.Start, span.Length);
        }
    }

    private async ValueTask<UsValue> EditSubscription(EventSymbol symbol, BoundNode left, BoundNode right, bool add, TextSpan span)
    {
        var handler = await ResolveHandler(right, span).ConfigureAwait(false);
        if (handler.Function.IsAsync || handler.Function.Template?.IsAsync == true)
        {
            throw new UsRuntimeException(UsRuntimeCodes.AsyncHandler, "Olay işleyicisi eşzamansız olamaz.", span.Start, span.Length);
        }

        var handlers = await HandlersOf(symbol, left, span).ConfigureAwait(false);
        if (add)
        {
            handlers.Add(handler);
            return UsValue.Unit;
        }

        var index = handlers.FindIndex(item => item.Same(handler));
        if (index < 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingSubscription, "Çıkarılacak dinleyici yok.", span.Start, span.Length);
        }

        handlers.RemoveAt(index);
        return UsValue.Unit;
    }

    private async ValueTask<List<UsHandler>> HandlersOf(EventSymbol symbol, BoundNode left, TextSpan span)
    {
        if (left.Kind == BoundKind.Member)
        {
            var receiver = await Evaluate(left.Children[0]).ConfigureAwait(false);
            return RequireInstance(receiver, span).Handlers(symbol);
        }

        return RequireInstance(Receiver(span), span).Handlers(symbol);
    }

    private async ValueTask<UsHandler> ResolveHandler(BoundNode node, TextSpan span)
    {
        if (node.Symbol is not FunctionSymbol function)
        {
            throw new UsRuntimeException(UsRuntimeCodes.AwaitTarget, "Olay işleyicisi fonksiyon olmalıdır.", span.Start, span.Length);
        }

        if (node.Kind == BoundKind.Member)
        {
            var receiver = await Evaluate(node.Children[0]).ConfigureAwait(false);
            return new UsHandler(function, RequireInstance(receiver, span));
        }

        if (function.DeclaringType is not null)
        {
            return new UsHandler(function, RequireInstance(Receiver(span), span));
        }

        return new UsHandler(function, null);
    }

    private async ValueTask<UsValue> Trigger(BoundNode node)
    {
        if (node.Symbol is not EventSymbol symbol)
        {
            throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Olay yok.", node.Span.Start, node.Span.Length);
        }

        var owner = RequireInstance(await Evaluate(node.Children[0]).ConfigureAwait(false), node.Children[0].Span);
        var argument = node.Children.Length > 1 ? await Evaluate(node.Children[1]).ConfigureAwait(false) : UsValue.Unit;
        var snapshot = owner.Handlers(symbol).ToArray();
        var errors = new List<Exception>();
        foreach (var handler in snapshot)
        {
            if (handler.Function.IsAsync || handler.Function.Template?.IsAsync == true)
            {
                throw new UsRuntimeException(UsRuntimeCodes.AsyncHandler, "Olay işleyicisi eşzamansız olamaz.", node.Span.Start, node.Span.Length);
            }

            try
            {
                var function = handler.Receiver is null ? handler.Function : Dispatch(handler.Function, handler.Receiver);
                await InvokeValues(function, handler.Receiver, [argument], node.Span).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count == 1)
        {
            throw errors[0];
        }

        if (errors.Count > 1)
        {
            throw new UsAggregateException(errors, node.Span.Start, node.Span.Length);
        }

        return UsValue.Unit;
    }

    private async ValueTask InvokeValues(FunctionSymbol function, UsInstance? receiver, UsValue[] values, TextSpan span)
    {
        var template = function.Template ?? function;
        if (receiver is not null)
        {
            receiver.EnsureActive();
            function = Dispatch(template, receiver);
            template = function.Template ?? function;
            _state.Receivers.Push(UsValue.FromReference(receiver));
        }

        try
        {
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
                await Execute(body).ConfigureAwait(false);
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

    private async ValueTask Concurrent(BoundNode node)
    {
        var waves = Waves(node);
        var cells = CaptureCells();
        var keys = CaptureKeys();
        var parent = _state;
        Exit();
        var errors = new List<Exception>();
        try
        {
            foreach (var wave in waves)
            {
                var tasks = wave.Select(statement => RunChild(statement, parent)).ToArray();
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var error in results)
                {
                    if (error is not null)
                    {
                        errors.Add(error);
                    }
                }

                if (errors.Count > 0)
                {
                    break;
                }
            }
        }
        finally
        {
            await EnterAsync().ConfigureAwait(false);
            _state = parent;
        }

        if (errors.Count == 0)
        {
            return;
        }

        Restore(cells, keys);
        if (errors.Count == 1)
        {
            throw errors[0];
        }

        throw new UsAggregateException(errors, node.Span.Start, node.Span.Length);
    }

    private async Task<Exception?> RunChild(BoundNode statement, ExecState parent)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _state = parent.Fork();
        _state.OwnsGate = true;
        try
        {
            await Execute(statement).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (exception is UsRuntimeException or UsThrown)
        {
            return exception;
        }
        finally
        {
            var owns = _state.OwnsGate;
            _state.OwnsGate = false;
            _state = parent;
            if (owns)
            {
                _gate.Release();
            }
        }
    }

    private static List<List<BoundNode>> Waves(BoundNode block)
    {
        var waves = new List<List<BoundNode>>();
        var current = new List<BoundNode>();
        var written = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        foreach (var statement in block.Children)
        {
            var reads = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
            var writes = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
            Collect(statement, reads, writes);
            if (current.Count > 0 && reads.Any(written.Contains))
            {
                waves.Add(current);
                current = [];
                written.Clear();
            }

            current.Add(statement);
            written.UnionWith(writes);
        }

        if (current.Count > 0)
        {
            waves.Add(current);
        }

        return waves;
    }

    private static void Collect(BoundNode node, HashSet<Symbol> reads, HashSet<Symbol> writes)
    {
        if (node.Kind == BoundKind.Local && node.Symbol is not null)
        {
            writes.Add(node.Symbol);
        }

        if (node.Kind == BoundKind.Assignment && node.Children.Length > 0 && node.Children[0].Symbol is Symbol target)
        {
            writes.Add(target);
        }

        if (node.Kind == BoundKind.Name && node.Operator is null && node.Symbol is not null)
        {
            reads.Add(node.Symbol);
        }

        foreach (var child in node.Children)
        {
            Collect(child, reads, writes);
        }
    }

    private List<(Cell Cell, UsValue Value, bool Assigned)> CaptureCells()
    {
        var list = new List<(Cell, UsValue, bool)>();
        foreach (var frame in _state.Frames)
        {
            foreach (var cell in frame.Values)
            {
                cell.Capture(out var value, out var assigned);
                list.Add((cell, value, assigned));
            }
        }

        return list;
    }

    private List<(Dictionary<Symbol, Cell> Frame, Symbol Symbol)> CaptureKeys()
    {
        var list = new List<(Dictionary<Symbol, Cell>, Symbol)>();
        foreach (var frame in _state.Frames)
        {
            foreach (var pair in frame)
            {
                list.Add((frame, pair.Key));
            }
        }

        return list;
    }

    private void Restore(List<(Cell Cell, UsValue Value, bool Assigned)> cells, List<(Dictionary<Symbol, Cell> Frame, Symbol Symbol)> keys)
    {
        foreach (var (cell, value, assigned) in cells)
        {
            cell.Restore(value, assigned);
        }

        foreach (var frame in _state.Frames)
        {
            var remove = frame.Keys.Where(key => !keys.Any(item => ReferenceEquals(item.Frame, frame) && ReferenceEquals(item.Symbol, key))).ToArray();
            foreach (var key in remove)
            {
                frame.Remove(key);
            }
        }
    }
}
