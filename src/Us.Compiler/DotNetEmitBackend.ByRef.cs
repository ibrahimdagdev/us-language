using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static readonly Type UsValueByRefType = typeof(UsValue).MakeByRefType();

    private static bool IsByRefParameter(ParameterSymbol parameter) => parameter.IsRef || parameter.IsOut;

    private static bool HasByRefParameters(FunctionSymbol function) =>
        function.Parameters.Any(IsByRefParameter);

    private static Type[] ClrParameterTypes(FunctionSymbol function, bool instanceReceiver)
    {
        var offset = instanceReceiver ? 1 : 0;
        var types = new Type[function.Parameters.Count + offset];
        if (instanceReceiver)
        {
            types[0] = typeof(UsValue);
        }

        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            types[index + offset] = IsByRefParameter(parameter) ? UsValueByRefType : typeof(UsValue);
        }

        return types;
    }

    private static void ApplyByRefParameterAttributes(MethodBuilder method, FunctionSymbol function, bool instanceReceiver)
    {
        var positionOffset = instanceReceiver ? 2 : 1;
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            if (!IsByRefParameter(parameter))
            {
                continue;
            }

            var attributes = parameter.IsOut ? ParameterAttributes.Out : ParameterAttributes.None;
            method.DefineParameter(index + positionOffset, attributes, parameter.Name);
        }
    }

    private static Diagnostic AsyncByRefUnsupported(FunctionSymbol function, TextSpan span) =>
        new(
            DiagnosticCodes.EmitUnsupported,
            DiagnosticSeverity.Error,
            $"'{function.Name}' eşzamansız fonksiyonda ref/çıktı henüz derlenmiyor.",
            null,
            span,
            1,
            1,
            "Parity matrisi: docs/compiler-parity.md");

    private static Diagnostic FieldByRefUnsupported(BoundNode node) =>
        new(
            DiagnosticCodes.EmitUnsupported,
            DiagnosticSeverity.Error,
            "Alan ref/çıktı hedefi henüz derlenmiyor.",
            null,
            node.Span,
            1,
            1,
            "Parity matrisi: docs/compiler-parity.md");

    private static void EmitLoadByRefParameter(int argumentIndex, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldarg, argumentIndex);
        context.Il.Emit(OpCodes.Ldobj, typeof(UsValue));
    }

    private static void EmitStoreByRefParameter(int argumentIndex, Action loadValue, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldarg, argumentIndex);
        loadValue();
        context.Il.Emit(OpCodes.Stobj, typeof(UsValue));
    }

    /// <summary>
    /// Pushes a managed pointer to UsValue for a ref/çıktı argument (local, value param, or ByRef forward).
    /// </summary>
    private static void EmitArgumentAddress(BoundNode argument, EmitContext context)
    {
        if (argument.Symbol is FieldSymbol)
        {
            context.Diagnostics.Add(FieldByRefUnsupported(argument));
            // Keep the evaluation stack balanced for subsequent Call: push a dummy local address.
            var dummy = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Ldloca, dummy);
            return;
        }

        if (argument.Symbol is not null && context.ByRefArgIndexes.TryGetValue(argument.Symbol, out var byRefIndex))
        {
            context.Il.Emit(OpCodes.Ldarg, byRefIndex);
            return;
        }

        if (argument.Symbol is not null && context.Locals.TryGetValue(argument.Symbol, out var local))
        {
            context.Il.Emit(OpCodes.Ldloca, local);
            return;
        }

        context.Diagnostics.Add(Unsupported(argument, "ref/çıktı adresi üretilemedi."));
        var fallback = context.Il.DeclareLocal(typeof(UsValue));
        context.Il.Emit(OpCodes.Ldloca, fallback);
    }

    private static void BindByRefParameters(FunctionSymbol function, EmitContext context, int argumentOffset)
    {
        for (var index = 0; index < function.Parameters.Count; index++)
        {
            var parameter = function.Parameters[index];
            if (!IsByRefParameter(parameter))
            {
                continue;
            }

            context.ByRefArgIndexes[parameter] = index + argumentOffset;
        }
    }

    private static void EmitValueParameterProlog(
        FunctionSymbol function,
        ILGenerator bodyIl,
        EmitContext context,
        int argumentOffset)
    {
        for (var parameterIndex = 0; parameterIndex < function.Parameters.Count; parameterIndex++)
        {
            var parameter = function.Parameters[parameterIndex];
            if (IsByRefParameter(parameter))
            {
                continue;
            }

            var local = bodyIl.DeclareLocal(typeof(UsValue));
            bodyIl.Emit(OpCodes.Ldarg, parameterIndex + argumentOffset);
            EmitConvertForParameter(parameter.Type, parameter, bodyIl, context);
            bodyIl.Emit(OpCodes.Stloc, local);
            context.Locals[parameter] = local;
        }
    }
}
