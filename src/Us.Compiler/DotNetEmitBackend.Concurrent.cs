using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static void EmitConcurrent(BoundNode node, EmitContext context)
    {
        if (!context.IsAsyncMoveNext)
        {
            context.Diagnostics.Add(Unsupported(node, "eşişlem yalnız asenkron bağlamda derlenir."));
            return;
        }

        // Flatten to a single wave of independent statements when possible; dependent waves run sequentially.
        var waves = BuildWaves(node);
        foreach (var wave in waves)
        {
            EmitConcurrentWave(wave, context);
        }
    }

    private static void EmitConcurrentWave(IReadOnlyList<BoundNode> wave, EmitContext context)
    {
        if (wave.Count == 0)
        {
            return;
        }

        if (wave.Count == 1)
        {
            EmitStatement(wave[0], context);
            return;
        }

        var workers = new MethodBuilder[wave.Count];
        for (var index = 0; index < wave.Count; index++)
        {
            var method = DefineAsyncMoveNext(context.ProgramType, "UsConcurrent_" + context.LambdaSerial++ + "_" + index);
            workers[index] = method;
            EmitAsyncMoveNextBody(
                method,
                [wave[index]],
                context.Methods,
                context.ProgramType,
                returnsValue: false,
                context.Diagnostics,
                seed =>
                {
                    foreach (var pair in context.AsyncSlots)
                    {
                        seed.AsyncSlots[pair.Key] = pair.Value;
                    }

                    seed.NextAsyncSlot = context.NextAsyncSlot;
                });
        }

        // Build Action<UsAsyncBox>[]
        context.Il.Emit(OpCodes.Ldc_I4, wave.Count);
        context.Il.Emit(OpCodes.Newarr, typeof(Action<UsAsyncBox>));
        for (var index = 0; index < wave.Count; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(OpCodes.Ldnull);
            context.Il.Emit(OpCodes.Ldftn, workers[index]);
            context.Il.Emit(OpCodes.Newobj, typeof(Action<UsAsyncBox>).GetConstructor([typeof(object), typeof(IntPtr)])!);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        var workersLocal = context.Il.DeclareLocal(typeof(Action<UsAsyncBox>[]));
        context.Il.Emit(OpCodes.Stloc, workersLocal);

        // ConcurrentOnSlots(box.Slots, workers)
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Ldfld, typeof(UsAsyncBox).GetField(nameof(UsAsyncBox.Slots))!);
        context.Il.Emit(OpCodes.Ldloc, workersLocal);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.ConcurrentOnSlots))!);

        // Await the returned Task
        var taskLocal = context.Il.DeclareLocal(typeof(Task));
        context.Il.Emit(OpCodes.Stloc, taskLocal);
        context.Il.Emit(OpCodes.Ldloc, taskLocal);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Box), [typeof(Task)])!);
        EmitAwaitValueOnStack(context);
    }

    private static void EmitAwaitValueOnStack(EmitContext context)
    {
        // UsValue on stack.
        if (!context.IsAsyncMoveNext || context.AwaitResumeLabels is null || context.TryDepth > 0)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.AwaitInExceptionRegion))!);
            context.Il.Emit(OpCodes.Pop);
            return;
        }

        var state = context.NextAwaitState++;
        if (state >= context.AwaitResumeLabels.Length)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.AwaitInExceptionRegion))!);
            context.Il.Emit(OpCodes.Pop);
            return;
        }

        var valueLocal = context.Il.DeclareLocal(typeof(UsValue));
        context.Il.Emit(OpCodes.Stloc, valueLocal);
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Ldloc, valueLocal);
        context.Il.Emit(OpCodes.Ldc_I4, state);
        context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.Await))!);
        var afterSync = context.Il.DefineLabel();
        context.Il.Emit(OpCodes.Brfalse, afterSync);
        EmitAsyncSuspendReturn(context);
        context.Il.MarkLabel(context.AwaitResumeLabels[state]);
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.ResumeAwait))!);
        context.Il.Emit(OpCodes.Br, afterSync);
        context.Il.MarkLabel(afterSync);
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Ldfld, typeof(UsAsyncBox).GetField(nameof(UsAsyncBox.Current))!);
        context.Il.Emit(OpCodes.Pop);
    }

    private static List<List<BoundNode>> BuildWaves(BoundNode block)
    {
        var waves = new List<List<BoundNode>>();
        var current = new List<BoundNode>();
        var written = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        foreach (var statement in block.Children)
        {
            var reads = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
            var writes = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
            CollectSymbols(statement, reads, writes);
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

    private static void CollectSymbols(BoundNode node, HashSet<Symbol> reads, HashSet<Symbol> writes)
    {
        if (node.Kind == BoundKind.Local && node.Symbol is not null)
        {
            writes.Add(node.Symbol);
        }

        if (node.Kind == BoundKind.Assignment && node.Children.Length > 0 && node.Children[0].Symbol is { } target)
        {
            writes.Add(target);
        }

        if (node.Kind == BoundKind.Name && node.Operator is null && node.Symbol is not null)
        {
            reads.Add(node.Symbol);
        }

        if (!node.Children.IsDefaultOrEmpty)
        {
            foreach (var child in node.Children)
            {
                CollectSymbols(child, reads, writes);
            }
        }
    }
}

file sealed class ReferenceEqualityComparer : IEqualityComparer<Symbol>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(Symbol? x, Symbol? y) => ReferenceEquals(x, y);

    public int GetHashCode(Symbol obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
