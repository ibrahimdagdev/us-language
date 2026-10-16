using System.Runtime.CompilerServices;

namespace Us.Runtime;

/// <summary>
/// Opaque async handle for compiled US programs. Never exposed as a US source type.
/// </summary>
public sealed class UsOperation
{
    public UsOperation(Task<UsValue> task) => Task = task ?? throw new ArgumentNullException(nameof(task));

    public Task<UsValue> Task { get; }

    public static UsOperation From(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new UsOperation(AwaitUnit(task));
    }

    private static async Task<UsValue> AwaitUnit(Task task)
    {
        await task.ConfigureAwait(false);
        return UsValue.Unit;
    }
}

/// <summary>
/// Shared async helpers for DotNetEmitBackend. Real Task suspension; no busy-wait.
/// </summary>
public static class UsEmitAsync
{
    public static IUsScheduler Scheduler { get; set; } = TaskDelayScheduler.Instance;

    public static UsValue Box(Task<UsValue> task) => UsValue.FromReference(new UsOperation(task));

    public static UsValue Box(Task task) => UsValue.FromReference(UsOperation.From(task));

    public static UsValue Duration(UsValue magnitude, string unit, int start, int length)
    {
        long scale = unit switch
        {
            "milisaniye" => 1,
            "saniye" => 1000,
            "dakika" => 60_000,
            "saat" => 3_600_000,
            "gün" => 86_400_000,
            "hafta" => 604_800_000,
            _ => throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, $"Bilinmeyen süre birimi '{unit}'.", start, length),
        };

        long amount = magnitude.Kind switch
        {
            UsValueKind.Int => magnitude.Int,
            UsValueKind.UInt => magnitude.UInt,
            UsValueKind.Float64 => (long)magnitude.Float64,
            UsValueKind.Float32 => (long)magnitude.Float32,
            UsValueKind.Decimal => (long)magnitude.Decimal,
            _ => throw new UsRuntimeException(UsRuntimeCodes.RuntimeCondition, "Süre büyüklüğü sayı olmalıdır.", start, length),
        };

        try
        {
            checked
            {
                return UsValue.FromReference(new UsDuration(amount * scale));
            }
        }
        catch (OverflowException)
        {
            throw new UsRuntimeException(UsRuntimeCodes.DurationOverflow, "Süre taştı.", start, length);
        }
    }

    public static Task<UsValue> ToTask(UsValue value)
    {
        if (value.Reference is UsDuration duration)
        {
            return DelayAsync(duration);
        }

        if (value.Reference is UsOperation operation)
        {
            return operation.Task;
        }

        return Task.FromResult(value);
    }

    public static async Task<UsValue> DelayAsync(UsDuration duration)
    {
        await Scheduler.DelayAsync(duration.Milliseconds).ConfigureAwait(false);
        return UsValue.Unit;
    }

    /// <summary>
    /// CLR PE entry bridge for void Main. Async work itself uses real await suspension;
    /// this only blocks the process entry thread until the task completes.
    /// </summary>
    public static void RunEntry(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Used when await appears inside a CLR exception region (try/catch), where
    /// suspending the async state machine with a resume label would be invalid IL.
    /// Still waits on the real Task (no spin); not used for ordinary async methods.
    /// </summary>
    public static UsValue AwaitInExceptionRegion(UsValue value) =>
        ToTask(value).ConfigureAwait(false).GetAwaiter().GetResult();

    public static Task Start(Action<UsAsyncBox> moveNext) => Start(moveNext, null);

    public static int StartCalls;

    public static Task Start(Action<UsAsyncBox> moveNext, UsValue[]? initialSlots)
    {
        ArgumentNullException.ThrowIfNull(moveNext);
        StartCalls++;
        var stateMachine = new UsAsyncBox(moveNext, hasResult: false);
        SeedSlots(stateMachine, initialSlots);
        // Important: do not use AsyncTaskMethodBuilder.Start on a class with a nested Action —
        // invoke the body once, and let AwaitUnsafeOnCompleted resume via IAsyncStateMachine.MoveNext.
        stateMachine.MoveNext();
        return stateMachine.Builder.Task;
    }

    public static Task<UsValue> StartValue(Action<UsAsyncBox> moveNext) => StartValue(moveNext, null);

    public static Task<UsValue> StartValue(Action<UsAsyncBox> moveNext, UsValue[]? initialSlots)
    {
        ArgumentNullException.ThrowIfNull(moveNext);
        StartCalls++;
        var stateMachine = new UsAsyncBox(moveNext, hasResult: true);
        SeedSlots(stateMachine, initialSlots);
        stateMachine.MoveNext();
        return stateMachine.BuilderT.Task;
    }

    private static void SeedSlots(UsAsyncBox stateMachine, UsValue[]? initialSlots)
    {
        if (initialSlots is null || initialSlots.Length == 0)
        {
            return;
        }

        stateMachine.EnsureSlots(initialSlots.Length);
        Array.Copy(initialSlots, stateMachine.Slots, initialSlots.Length);
    }

    /// <summary>
    /// Concurrent wave on shared slots: all workers start before any completion is awaited.
    /// Errors aggregate in source order; on failure slots are restored to the pre-wave snapshot.
    /// </summary>
    public static async Task ConcurrentOnSlots(UsValue[] slots, Action<UsAsyncBox>[] workers)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(workers);
        var snapshot = new UsValue[slots.Length];
        Array.Copy(slots, snapshot, slots.Length);

        var boxes = new UsAsyncBox[workers.Length];
        var tasks = new Task[workers.Length];
        for (var i = 0; i < workers.Length; i++)
        {
            var box = new UsAsyncBox(workers[i], hasResult: false);
            box.ShareSlots(slots);
            boxes[i] = box;
            box.MoveNext();
            tasks[i] = box.Builder.Task;
        }

        var errors = new Exception?[workers.Length];
        var faulted = false;
        for (var i = 0; i < tasks.Length; i++)
        {
            try
            {
                await tasks[i].ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors[i] = exception;
                faulted = true;
            }
        }

        if (!faulted)
        {
            return;
        }

        Array.Copy(snapshot, slots, slots.Length);
        var ordered = errors.Where(item => item is not null).Cast<Exception>().ToArray();
        throw new UsAggregateException(ordered, 0, 0);
    }
}

public sealed class UsAsyncBox : IAsyncStateMachine
{
    private readonly Action<UsAsyncBox> _moveNext;
    private readonly bool _hasResult;
    private TaskAwaiter<UsValue> _awaiter;

    public UsAsyncBox(Action<UsAsyncBox> moveNext, bool hasResult)
    {
        _moveNext = moveNext;
        _hasResult = hasResult;
        Builder = AsyncTaskMethodBuilder.Create();
        BuilderT = AsyncTaskMethodBuilder<UsValue>.Create();
        Slots = new UsValue[32];
        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i] = UsValue.Unit;
        }
    }

    public AsyncTaskMethodBuilder Builder;
    public AsyncTaskMethodBuilder<UsValue> BuilderT;
    public int State;
    public UsValue[] Slots;
    public UsValue Current;

    public void EnsureSlots(int count)
    {
        if (Slots.Length >= count)
        {
            return;
        }

        var next = new UsValue[count];
        Array.Copy(Slots, next, Slots.Length);
        for (var i = Slots.Length; i < count; i++)
        {
            next[i] = UsValue.Unit;
        }

        Slots = next;
    }

    public void ShareSlots(UsValue[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Slots = slots;
    }

    public UsValue GetSlot(int index) => Slots[index];

    public void SetSlot(int index, UsValue value) => Slots[index] = value;

    /// <summary>
    /// Returns true when suspension is scheduled (caller must return from MoveNext).
    /// </summary>
    public bool Await(UsValue value, int nextState)
    {
        var task = UsEmitAsync.ToTask(value);
        _awaiter = task.GetAwaiter();
        if (!_awaiter.IsCompleted)
        {
            State = nextState;
            var self = this;
            if (_hasResult)
            {
                BuilderT.AwaitUnsafeOnCompleted(ref _awaiter, ref self);
            }
            else
            {
                Builder.AwaitUnsafeOnCompleted(ref _awaiter, ref self);
            }

            return true;
        }

        Current = _awaiter.GetResult();
        return false;
    }

    public void ResumeAwait()
    {
        Current = _awaiter.GetResult();
        // Prevent accidental re-entry into the same resume label.
        State = int.MinValue;
    }

    private bool _completed;

    public void Complete()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        if (_hasResult)
        {
            BuilderT.SetResult(Current);
        }
        else
        {
            Builder.SetResult();
        }
    }

    public void CompleteValue(UsValue value)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        Current = value;
        BuilderT.SetResult(value);
    }

    public void Fail(Exception exception)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        if (_hasResult)
        {
            BuilderT.SetException(exception);
        }
        else
        {
            Builder.SetException(exception);
        }
    }

    public void MoveNext()
    {
        if (_completed)
        {
            return;
        }

        try
        {
            _moveNext(this);
        }
        catch (Exception exception)
        {
            Fail(exception);
        }
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        if (_hasResult)
        {
            BuilderT.SetStateMachine(stateMachine);
        }
        else
        {
            Builder.SetStateMachine(stateMachine);
        }
    }
}
