using System.Reflection.Emit;
using System.Text.Json;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static void EmitBootstrap(ILGenerator il)
    {
        il.Emit(OpCodes.Call, typeof(UsEmitHost).GetMethod(nameof(UsEmitHost.BootstrapFromEnvironment))!);
    }

    private static void EmitBindSetting(BoundNode node, EmitContext context)
    {
        if (node.Symbol is not SourceTypeSymbol type || type.ConfigSection is null)
        {
            context.Diagnostics.Add(Unsupported(node, "bağla ayar modeli yok."));
            return;
        }

        var fields = CollectFields(type);
        context.Il.Emit(OpCodes.Ldstr, type.ConfigSection);
        context.Il.Emit(OpCodes.Ldstr, type.Name);
        EmitStringArray(fields.Select(field => field.Name).ToArray(), context);
        EmitStringArray(fields.Select(DescribeFieldType).ToArray(), context);
        EmitBoolArray(fields.Select(field => field.Type.IsNullable).ToArray(), context);
        EmitBoolArray(fields.Select(field => field.IsRequired).ToArray(), context);
        EmitStringArray(fields.Select(EncodeDefaultJson).ToArray(), context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.BindSetting))!);
    }

    private static bool TryEmitClockName(BoundNode node, EmitContext context)
    {
        if (node.Symbol is not VariableSymbol variable)
        {
            return false;
        }

        if (variable.Name == "bugün")
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Today))!);
            return true;
        }

        if (variable.Name == "şimdi")
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Now))!);
            return true;
        }

        return false;
    }

    private static bool TryEmitTemporalBinary(BoundNode node, EmitContext context)
    {
        var leftNode = node.Children[0];
        var rightNode = node.Children[1];
        if (!IsTemporalType(leftNode.Type) && !IsTemporalType(rightNode.Type))
        {
            return false;
        }

        var op = node.Operator;
        EmitExpression(leftNode, context);
        EmitExpression(rightNode, context);

        if (op is TokenKind.Plus)
        {
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Add))!);
            return true;
        }

        if (op is TokenKind.Minus)
        {
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Subtract))!);
            return true;
        }

        if (op is TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Less or TokenKind.LessEqual
            or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            context.Il.Emit(OpCodes.Ldstr, OperatorText(op.Value));
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Compare))!);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
            return true;
        }

        context.Il.Emit(OpCodes.Pop);
        context.Il.Emit(OpCodes.Pop);
        context.Diagnostics.Add(Unsupported(node, "Zamansal işlemci desteklenmiyor."));
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        return true;
    }

    private static bool TryEmitStdlibIntrinsic(BoundNode node, FunctionSymbol function, EmitContext context)
    {
        if (function.Intrinsic == Intrinsic.None)
        {
            return false;
        }

        var offset = 0;
        if (node.Children.Length > function.Parameters.Count && node.Children[0].Symbol is ModuleSymbol)
        {
            offset = 1;
        }

        switch (function.Intrinsic)
        {
            case Intrinsic.TextTrim:
                EmitReceiver(node, function, offset, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Trim))!);
                return true;
            case Intrinsic.TextUpper:
                EmitReceiver(node, function, offset, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Upper))!);
                return true;
            case Intrinsic.TextLower:
                EmitReceiver(node, function, offset, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Lower))!);
                return true;
            case Intrinsic.TextContains:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Contains))!);
                return true;
            case Intrinsic.TextStarts:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Starts))!);
                return true;
            case Intrinsic.TextEnds:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Ends))!);
                return true;
            case Intrinsic.TextReplace:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                EmitParameter(node, function, offset, 1, context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Replace))!);
                return true;
            case Intrinsic.TextSplit:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Split))!);
                return true;
            case Intrinsic.TextJoin:
                EmitReceiver(node, function, offset, context);
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Join))!);
                return true;
            case Intrinsic.JsonWrite:
                EmitParameter(node, function, offset, 0, context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitJson).GetMethod(nameof(UsEmitJson.Write))!);
                return true;
            case Intrinsic.JsonRead:
                EmitParameter(node, function, offset, 0, context);
                EmitJsonReadShape(node.Type, context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitJson).GetMethod(nameof(UsEmitJson.Read))!);
                return true;
            case Intrinsic.FileRead:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCall(node, nameof(UsEmitStdlib.FileRead), context);
                return true;
            case Intrinsic.FileWrite:
                EmitParameter(node, function, offset, 0, context);
                EmitParameter(node, function, offset, 1, context);
                EmitSpanCallVoid(node, nameof(UsEmitStdlib.FileWrite), context);
                return true;
            case Intrinsic.FileAppend:
                EmitParameter(node, function, offset, 0, context);
                EmitParameter(node, function, offset, 1, context);
                EmitSpanCallVoid(node, nameof(UsEmitStdlib.FileAppend), context);
                return true;
            case Intrinsic.FileDelete:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCallVoid(node, nameof(UsEmitStdlib.FileDelete), context);
                return true;
            case Intrinsic.FileExists:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCall(node, nameof(UsEmitStdlib.FileExists), context);
                return true;
            case Intrinsic.DirectoryCreate:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCallVoid(node, nameof(UsEmitStdlib.DirectoryCreate), context);
                return true;
            case Intrinsic.DirectoryDelete:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCallVoid(node, nameof(UsEmitStdlib.DirectoryDelete), context);
                return true;
            case Intrinsic.DirectoryExists:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCall(node, nameof(UsEmitStdlib.DirectoryExists), context);
                return true;
            case Intrinsic.DirectoryFiles:
                EmitParameter(node, function, offset, 0, context);
                EmitSpanCall(node, nameof(UsEmitStdlib.DirectoryFiles), context);
                return true;
            case Intrinsic.PathCombine:
                EmitParameter(node, function, offset, 0, context);
                EmitParameter(node, function, offset, 1, context);
                EmitSpanCall(node, nameof(UsEmitStdlib.PathCombine), context);
                return true;
            case Intrinsic.LogTrace:
                EmitLog(0, node, function, offset, context);
                return true;
            case Intrinsic.LogInfo:
                EmitLog(1, node, function, offset, context);
                return true;
            case Intrinsic.LogWarning:
                EmitLog(2, node, function, offset, context);
                return true;
            case Intrinsic.LogError:
                EmitLog(3, node, function, offset, context);
                return true;
            case Intrinsic.LogCritical:
                EmitLog(4, node, function, offset, context);
                return true;
            default:
                return false;
        }
    }

    private static void EmitLog(int level, BoundNode node, FunctionSymbol function, int offset, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, level);
        var argumentIndex = offset;
        if (argumentIndex < node.Children.Length && node.Children[argumentIndex].Symbol is FieldSymbol field)
        {
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            EmitParameter(node, function, offset, 0, context);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.LogNamed))!);
        }
        else
        {
            EmitParameter(node, function, offset, 0, context);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(nameof(UsEmitStdlib.Log))!);
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitSpanCall(BoundNode node, string methodName, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitStdlib).GetMethod(methodName)!);
    }

    private static void EmitSpanCallVoid(BoundNode node, string methodName, EmitContext context)
    {
        EmitSpanCall(node, methodName, context);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitReceiver(BoundNode node, FunctionSymbol function, int offset, EmitContext context)
    {
        // Text methods: receiver is the first child when children exceed parameters (no module).
        if (offset == 0 && node.Children.Length > function.Parameters.Count)
        {
            EmitExpression(node.Children[0], context);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitParameter(BoundNode node, FunctionSymbol function, int offset, int parameterIndex, EmitContext context)
    {
        var childIndex = offset + parameterIndex;
        // Text methods push receiver at 0; parameters follow.
        if (offset == 0 && node.Children.Length > function.Parameters.Count)
        {
            childIndex = 1 + parameterIndex;
        }

        if (childIndex < node.Children.Length)
        {
            EmitExpression(node.Children[childIndex], context);
            return;
        }

        if (parameterIndex < function.Parameters.Count && function.Parameters[parameterIndex].Default is { } defaultNode)
        {
            EmitExpression(defaultNode, context);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitJsonReadShape(TypeSymbol type, EmitContext context)
    {
        if (type is ConstructedTypeSymbol list
            && list.Definition is GenericDefinitionSymbol { Name: "liste" }
            && list.Arguments.Length == 1)
        {
            context.Il.Emit(OpCodes.Ldstr, type.IsNullable ? "liste?" : "liste");
            EmitStringArray([], context);
            EmitStringArray([DescribeType(list.Arguments[0])], context);
            EmitBoolArray([], context);
            EmitBoolArray([], context);
            EmitStringArray([], context);
            return;
        }

        if (type.WithNullable(false) is SourceTypeSymbol source)
        {
            var fields = CollectFields(source);
            context.Il.Emit(OpCodes.Ldstr, type.IsNullable ? source.Name + "?" : source.Name);
            EmitStringArray(fields.Select(field => field.Name).ToArray(), context);
            EmitStringArray(fields.Select(DescribeFieldType).ToArray(), context);
            EmitBoolArray(fields.Select(field => field.Type.IsNullable).ToArray(), context);
            EmitBoolArray(fields.Select(field => field.IsRequired).ToArray(), context);
            EmitStringArray(fields.Select(EncodeDefaultJson).ToArray(), context);
            return;
        }

        var primitive = type.WithNullable(false).Name;
        context.Il.Emit(OpCodes.Ldstr, type.IsNullable ? primitive + "?" : primitive);
        EmitStringArray([], context);
        EmitStringArray([], context);
        EmitBoolArray([], context);
        EmitBoolArray([], context);
        EmitStringArray([], context);
    }

    private static string DescribeFieldType(FieldSymbol field) => DescribeType(field.Type);

    private static string DescribeType(TypeSymbol type)
    {
        var nullable = type.IsNullable;
        type = type.WithNullable(false);
        string core;
        if (type is ConstructedTypeSymbol list
            && list.Definition is GenericDefinitionSymbol { Name: "liste" }
            && list.Arguments.Length == 1)
        {
            core = "liste:" + DescribeType(list.Arguments[0]);
        }
        else if (type is SourceTypeSymbol source)
        {
            var fields = CollectFields(source);
            core = UsEmitJson.EncodeShape(
                source.Name,
                fields.Select(field => field.Name).ToArray(),
                fields.Select(DescribeFieldType).ToArray(),
                fields.Select(field => field.Type.IsNullable).ToArray(),
                fields.Select(field => field.IsRequired).ToArray(),
                fields.Select(EncodeDefaultJson).ToArray());
        }
        else
        {
            core = type.Name;
        }

        // Trailing '?' preserves nullability for nested/liste element ReadTyped (US1541 fidelity).
        return nullable ? core + "?" : core;
    }

    private static string EncodeDefaultJson(FieldSymbol field)
    {
        if (field.Default is null)
        {
            return string.Empty;
        }

        return EncodeLiteralJson(field.Default);
    }

    private static string EncodeLiteralJson(BoundNode node)
    {
        if (node.Kind != BoundKind.Literal || node.Constant is null)
        {
            return string.Empty;
        }

        if (node.Constant == "~")
        {
            return "null";
        }

        var name = node.Type.WithNullable(false).Name;
        return name switch
        {
            "metin" or "karakter" => JsonSerializer.Serialize(node.Constant),
            "mantıksal" => node.Constant == "doğru" ? "true" : "false",
            "tamsayı" or "utamsayı" or "ondalık" or "gondalık" or "hondalık" => node.Constant,
            _ => string.Empty,
        };
    }

    private static void EmitStringArray(string[] values, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, values.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(string));
        for (var index = 0; index < values.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(OpCodes.Ldstr, values[index]);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static void EmitBoolArray(bool[] values, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, values.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(bool));
        for (var index = 0; index < values.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(values[index] ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Stelem_I1);
        }
    }

    private static bool IsTemporalType(TypeSymbol type)
    {
        var name = type.WithNullable(false).Name;
        return name is "tarih" or "saat" or "tarihsaat" or "süre";
    }
}
