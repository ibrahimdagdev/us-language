using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static bool ContainsAwait(BoundNode node)
    {
        if (node.Kind == BoundKind.Unary && node.Operator == TokenKind.Bekle)
        {
            return true;
        }

        if (node.Kind == BoundKind.Block && node.Operator == TokenKind.Eesislem)
        {
            return true;
        }

        if (node.Children.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var child in node.Children)
        {
            if (ContainsAwait(child))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountAwaits(BoundNode node)
    {
        var count = 0;
        if (node.Kind == BoundKind.Unary && node.Operator == TokenKind.Bekle)
        {
            count++;
        }

        // eşişlem parent awaits ConcurrentOnSlots once (waves may add more; pad in Emit).
        if (node.Kind == BoundKind.Block && node.Operator == TokenKind.Eesislem)
        {
            count += 2;
        }

        if (!node.Children.IsDefaultOrEmpty)
        {
            foreach (var child in node.Children)
            {
                count += CountAwaits(child);
            }
        }

        return count;
    }

    private static bool TopLevelNeedsAsync(BoundCompilation compilation) =>
        compilation.Members.Any(ContainsAwait);

    private static MethodBuilder DefineAsyncMoveNext(TypeBuilder type, string name) =>
        type.DefineMethod(
            name,
            MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(UsAsyncBox)]);

    private static void EmitAsyncEntry(ILGenerator il, MethodBuilder moveNext, bool returnsValue)
    {
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, moveNext);
        il.Emit(OpCodes.Newobj, typeof(Action<UsAsyncBox>).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, returnsValue
            ? typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.StartValue), [typeof(Action<UsAsyncBox>)])!
            : typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Start), [typeof(Action<UsAsyncBox>)])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitAsyncFunctionTrampoline(
        ILGenerator il,
        FunctionSymbol function,
        MethodBuilder moveNext,
        bool returnsValue)
    {
        var count = function.Parameters.Count;
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Newarr, typeof(UsValue));
        for (var index = 0; index < count; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldarg, index);
            il.Emit(OpCodes.Stelem, typeof(UsValue));
        }

        EmitAsyncStartFromSlots(il, moveNext, returnsValue);
    }

    /// <summary>
    /// Instance async entry: slots[0] = self (bu), slots[1..] = parameters.
    /// </summary>
    private static void EmitAsyncInstanceTrampoline(
        ILGenerator il,
        FunctionSymbol function,
        MethodBuilder moveNext,
        bool returnsValue)
    {
        var count = function.Parameters.Count + 1;
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Newarr, typeof(UsValue));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem, typeof(UsValue));
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index + 1);
            il.Emit(OpCodes.Ldarg, index + 1);
            il.Emit(OpCodes.Stelem, typeof(UsValue));
        }

        EmitAsyncStartFromSlots(il, moveNext, returnsValue);
    }

    private static void EmitAsyncStartFromSlots(ILGenerator il, MethodBuilder moveNext, bool returnsValue)
    {
        var slots = il.DeclareLocal(typeof(UsValue[]));
        il.Emit(OpCodes.Stloc, slots);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, moveNext);
        il.Emit(OpCodes.Newobj, typeof(Action<UsAsyncBox>).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldloc, slots);
        il.Emit(OpCodes.Call, returnsValue
            ? typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.StartValue), [typeof(Action<UsAsyncBox>), typeof(UsValue[])])!
            : typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Start), [typeof(Action<UsAsyncBox>), typeof(UsValue[])])!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitAsyncMoveNextBody(
        MethodBuilder moveNext,
        IEnumerable<BoundNode> statements,
        Dictionary<FunctionSymbol, MethodBuilder> methods,
        TypeBuilder programType,
        bool returnsValue,
        List<Diagnostic> diagnostics,
        Action<EmitContext>? seedLocals = null,
        Action<EmitContext>? preface = null)
    {
        var bodyList = statements.ToArray();
        var awaitCount = bodyList.Sum(CountAwaits);
        var il = moveNext.GetILGenerator();
        var context = new EmitContext(il, methods, programType, returnsValue, isAsyncMoveNext: true)
        {
            AwaitResumeLabels = new Label[awaitCount + 1],
            AsyncExitLabel = il.DefineLabel(),
        };
        for (var i = 0; i < context.AwaitResumeLabels.Length; i++)
        {
            context.AwaitResumeLabels[i] = il.DefineLabel();
        }

        seedLocals?.Invoke(context);

        // No try/catch wrapper here: UsAsyncBox.MoveNext already catches and Fail()s.
        // Using Leave across a method-wide exception block interacted badly with await resume.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeof(UsAsyncBox).GetField(nameof(UsAsyncBox.State))!);
        il.Emit(OpCodes.Switch, context.AwaitResumeLabels);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "Geçersiz async durumu.");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.Fail))!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(context.AwaitResumeLabels[0]);
        preface?.Invoke(context);
        foreach (var statement in bodyList)
        {
            EmitStatement(statement, context);
        }

        il.Emit(OpCodes.Ldarg_0);
        if (returnsValue)
        {
            il.Emit(OpCodes.Ldfld, typeof(UsAsyncBox).GetField(nameof(UsAsyncBox.Current))!);
            il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.CompleteValue))!);
        }
        else
        {
            il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.Complete))!);
        }

        if (context.TryDepth > 0)
        {
            il.Emit(OpCodes.Leave, context.AsyncExitLabel);
        }
        else
        {
            il.Emit(OpCodes.Br, context.AsyncExitLabel);
        }

        // Mark unused resume labels so CreateType does not fail.
        if (context.AwaitResumeLabels is not null)
        {
            for (var i = context.NextAwaitState; i < context.AwaitResumeLabels.Length; i++)
            {
                il.MarkLabel(context.AwaitResumeLabels[i]);
                il.Emit(OpCodes.Br, context.AsyncExitLabel);
            }
        }

        il.MarkLabel(context.AsyncExitLabel);
        il.Emit(OpCodes.Ret);

        diagnostics.AddRange(context.Diagnostics);
    }

    private static void EmitAwaitUnary(BoundNode node, EmitContext context)
    {
        if (!context.IsAsyncMoveNext || context.AwaitResumeLabels is null)
        {
            context.Diagnostics.Add(Unsupported(node, "bekle yalnız asenkron bağlamda derlenir."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        EmitExpression(node.Children[0], context);

        // Cannot place resume labels inside CLR exception regions.
        if (context.TryDepth > 0)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.AwaitInExceptionRegion))!);
            return;
        }

        var state = context.NextAwaitState++;
        if (state >= context.AwaitResumeLabels.Length)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.AwaitInExceptionRegion))!);
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

        // Suspended resume path (switch target).
        context.Il.MarkLabel(context.AwaitResumeLabels[state]);
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.ResumeAwait))!);
        context.Il.Emit(OpCodes.Br, afterSync);

        // Synchronous completion path (Await returned false).
        context.Il.MarkLabel(afterSync);
        context.Il.Emit(OpCodes.Ldarg_0);
        context.Il.Emit(OpCodes.Ldfld, typeof(UsAsyncBox).GetField(nameof(UsAsyncBox.Current))!);
    }

    private static void EmitAsyncSuspendReturn(EmitContext context)
    {
        if (context.TryDepth > 0)
        {
            context.Il.Emit(OpCodes.Leave, context.AsyncExitLabel);
        }
        else
        {
            context.Il.Emit(OpCodes.Ret);
        }
    }

    private static void EmitLoadLocal(Symbol symbol, EmitContext context)
    {
        // Temporary IL locals (e.g. constructor params) take precedence over async slots.
        if (context.Locals.TryGetValue(symbol, out var local))
        {
            context.Il.Emit(OpCodes.Ldloc, local);
            return;
        }

        if (context.IsAsyncMoveNext)
        {
            var slot = context.AllocateSlot(symbol);
            context.Il.Emit(OpCodes.Ldarg_0);
            context.Il.Emit(OpCodes.Ldc_I4, slot);
            context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.GetSlot))!);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitStoreLocal(Symbol symbol, EmitContext context)
    {
        if (context.Locals.TryGetValue(symbol, out var local))
        {
            context.Il.Emit(OpCodes.Stloc, local);
            return;
        }

        if (context.IsAsyncMoveNext)
        {
            var slot = context.AllocateSlot(symbol);
            var tmp = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, tmp);
            context.Il.Emit(OpCodes.Ldarg_0);
            context.Il.Emit(OpCodes.Ldc_I4, slot);
            context.Il.Emit(OpCodes.Ldloc, tmp);
            context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.SetSlot))!);
            return;
        }

        context.Il.Emit(OpCodes.Pop);
    }
}
