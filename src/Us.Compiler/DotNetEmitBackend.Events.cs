using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static string MethodSlotKey(FunctionSymbol function)
    {
        var typeName = function.DeclaringType?.Name ?? "";
        return typeName + "\0" + function.Name + "\0" + function.Parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string VirtualSlotKey(FunctionSymbol function)
    {
        var current = function.Template ?? function;
        if (current.Targets.Count > 0)
        {
            current = current.Targets[^1];
        }

        return MethodSlotKey(current);
    }

    private static bool IsVirtualSlot(FunctionSymbol function)
    {
        var current = function.Template ?? function;
        return current.IsVirtual || current.IsAbstract || current.IsOverride || current.Targets.Count > 0;
    }

    private static FieldSymbol[] CollectFields(SourceTypeSymbol type)
    {
        var chain = new List<SourceTypeSymbol>();
        var seen = new HashSet<SourceTypeSymbol>();
        for (var current = type.Definition; current is not null && seen.Add(current.Definition); current = current.BaseType as SourceTypeSymbol)
        {
            chain.Add(current.Definition);
        }

        chain.Reverse();
        return chain.SelectMany(item => item.Fields).ToArray();
    }

    private static void EmitThis(EmitContext context)
    {
        if (context.ThisLocal is not null)
        {
            context.Il.Emit(OpCodes.Ldloc, context.ThisLocal);
            return;
        }

        if (context.IsAsyncMoveNext && context.ThisSlot is int slot)
        {
            context.Il.Emit(OpCodes.Ldarg_0);
            context.Il.Emit(OpCodes.Ldc_I4, slot);
            context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.GetSlot))!);
            return;
        }

        context.Diagnostics.Add(new Diagnostic(
            DiagnosticCodes.EmitUnsupported,
            DiagnosticSeverity.Error,
            "bu burada yok.",
            null,
            default,
            1,
            1,
            "Parity matrisi: docs/compiler-parity.md"));
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitFieldLoad(FieldSymbol field, TextSpan span, EmitContext context)
    {
        EmitThis(context);
        context.Il.Emit(OpCodes.Ldstr, field.Name);
        context.Il.Emit(OpCodes.Ldc_I4, span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.ReadField))!);
    }

    private static void EmitFieldStore(FieldSymbol field, Action loadValue, TextSpan span, EmitContext context)
    {
        EmitThis(context);
        context.Il.Emit(OpCodes.Ldstr, field.Name);
        loadValue();
        context.Il.Emit(OpCodes.Ldc_I4, span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
    }

    private static void EmitEventAssignment(BoundNode node, EmitContext context)
    {
        var left = node.Children[0];
        var right = node.Children[1];
        if (left.Symbol is not EventSymbol eventSymbol || right.Symbol is not FunctionSymbol function)
        {
            context.Diagnostics.Add(Unsupported(node, "Olay aboneliği geçersiz."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (function.IsAsync || function.Template?.IsAsync == true)
        {
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitEvents).GetMethod(nameof(UsEmitEvents.RejectAsyncHandler))!);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (left.Kind == BoundKind.Member && left.Children.Length > 0)
        {
            EmitExpression(left.Children[0], context);
        }
        else
        {
            EmitThis(context);
        }

        context.Il.Emit(OpCodes.Ldstr, eventSymbol.Name);
        context.Il.Emit(OpCodes.Ldstr, VirtualSlotKey(function));
        EmitHandlerReceiver(right, context);
        EmitHandlerAction(function, context);
        context.Il.Emit(IsVirtualSlot(function) ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        var methodName = node.Operator == TokenKind.PlusEquals
            ? nameof(UsEmitEvents.Add)
            : nameof(UsEmitEvents.Remove);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitEvents).GetMethod(methodName)!);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitHandlerReceiver(BoundNode right, EmitContext context)
    {
        if (right.Kind == BoundKind.Member && right.Children.Length > 0)
        {
            EmitExpression(right.Children[0], context);
            return;
        }

        if (right.Symbol is FunctionSymbol { DeclaringType: not null, IsStatic: false })
        {
            EmitThis(context);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitHandlerAction(FunctionSymbol function, EmitContext context)
    {
        var target = function.Template ?? function;
        if (!context.Methods.TryGetValue(target, out var method) && !context.Methods.TryGetValue(function, out method))
        {
            context.Diagnostics.Add(new Diagnostic(
                DiagnosticCodes.EmitUnsupported,
                DiagnosticSeverity.Error,
                $"Olay işleyicisi '{function.Name}' emit edilmedi.",
                null,
                default,
                1,
                1,
                "Parity matrisi: docs/compiler-parity.md"));
            context.Il.Emit(OpCodes.Ldnull);
            return;
        }

        var serial = context.LambdaSerial++;
        var trampoline = CreateActionTrampoline(context.ProgramType, method, target, serial);
        context.Il.Emit(OpCodes.Ldnull);
        context.Il.Emit(OpCodes.Ldftn, trampoline);
        context.Il.Emit(OpCodes.Newobj, typeof(Action<UsValue, UsValue>).GetConstructor([typeof(object), typeof(IntPtr)])!);
    }

    private static MethodBuilder CreateActionTrampoline(TypeBuilder programType, MethodInfo method, FunctionSymbol target, int serial)
    {
        var trampoline = programType.DefineMethod(
            "UsEvt_" + serial,
            MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(UsValue), typeof(UsValue)]);
        var il = trampoline.GetILGenerator();
        var returnsValue = target.ReturnType is not UnitTypeSymbol;
        if (target.DeclaringType is null)
        {
            for (var i = 0; i < target.Parameters.Count; i++)
            {
                il.Emit(i == 0 ? OpCodes.Ldarg_1 : OpCodes.Ldarg_1);
                if (i > 0)
                {
                    il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                }
            }

            il.Emit(OpCodes.Call, method);
        }
        else
        {
            il.Emit(OpCodes.Ldarg_0);
            if (target.Parameters.Count >= 1)
            {
                il.Emit(OpCodes.Ldarg_1);
            }

            for (var i = 1; i < target.Parameters.Count; i++)
            {
                il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }

            il.Emit(OpCodes.Call, method);
        }

        if (returnsValue)
        {
            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ret);
        return trampoline;
    }

    private static void EmitEventTrigger(BoundNode node, EmitContext context)
    {
        if (node.Symbol is not EventSymbol eventSymbol)
        {
            context.Diagnostics.Add(Unsupported(node, "Olay yok."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        EmitExpression(node.Children[0], context);
        context.Il.Emit(OpCodes.Ldstr, eventSymbol.Name);
        if (node.Children.Length > 1)
        {
            EmitExpression(node.Children[1], context);
        }
        else
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }

        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitEvents).GetMethod(nameof(UsEmitEvents.Trigger))!);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitVTableRegistration(ILGenerator il, FunctionSymbol function, MethodBuilder method, TypeBuilder programType, ref int serial)
    {
        if (function.DeclaringType is null || function.IsStatic)
        {
            return;
        }

        var typeName = function.DeclaringType.Name;
        var slot = VirtualSlotKey(function);
        var trampoline = CreateActionTrampoline(programType, method, function, serial++);

        il.Emit(OpCodes.Ldstr, typeName);
        il.Emit(OpCodes.Ldstr, slot);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, trampoline);
        il.Emit(OpCodes.Newobj, typeof(Action<UsValue, UsValue>).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, typeof(UsEmitVTable).GetMethod(nameof(UsEmitVTable.Register))!);

        il.Emit(OpCodes.Ldstr, typeName);
        if (function.DeclaringType.BaseType is SourceTypeSymbol baseType)
        {
            il.Emit(OpCodes.Ldstr, baseType.Name);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, typeof(UsEmitVTable).GetMethod(nameof(UsEmitVTable.SetBase))!);
    }
}
