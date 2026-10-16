using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Hosting;
using Us.Runtime;
using Us.Semantics;
using Task = System.Threading.Tasks.Task;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static bool TryEmitHttpIntrinsic(BoundNode node, FunctionSymbol function, EmitContext context)
    {
        switch (function.Intrinsic)
        {
            case Intrinsic.HttpOk:
                EmitHttpResult(200, node, function, context);
                return true;
            case Intrinsic.HttpCreated:
                EmitHttpResult(201, node, function, context);
                return true;
            case Intrinsic.HttpNoContent:
                context.Il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.ResultNoContent))!);
                return true;
            case Intrinsic.HttpBadRequest:
                EmitHttpResult(400, node, function, context);
                return true;
            case Intrinsic.HttpUnauthorized:
                EmitHttpResult(401, node, function, context);
                return true;
            case Intrinsic.HttpForbidden:
                EmitHttpResult(403, node, function, context);
                return true;
            case Intrinsic.HttpNotFound:
                EmitHttpResult(404, node, function, context);
                return true;
            case Intrinsic.HttpConflict:
                EmitHttpResult(409, node, function, context);
                return true;
            case Intrinsic.HttpHeader:
                EmitHttpHeader(node, function, context);
                return true;
            case Intrinsic.HttpGet:
                EmitHttpSend(node, function, "GET", context);
                return true;
            case Intrinsic.HttpPost:
                EmitHttpSend(node, function, "POST", context);
                return true;
            case Intrinsic.HttpPut:
                EmitHttpSend(node, function, "PUT", context);
                return true;
            case Intrinsic.HttpPatch:
                EmitHttpSend(node, function, "PATCH", context);
                return true;
            case Intrinsic.HttpDelete:
                EmitHttpSend(node, function, "DELETE", context);
                return true;
            case Intrinsic.HttpJson:
                EmitHttpJson(node, context);
                return true;
            default:
                return false;
        }
    }

    private static void EmitHttpResult(int status, BoundNode node, FunctionSymbol function, EmitContext context)
    {
        context.Il.Emit(OpCodes.Ldc_I4, status);
        var offset = HttpArgumentOffset(node, function);
        var bodyIndex = offset;
        if (bodyIndex < node.Children.Length)
        {
            EmitExpression(node.Children[bodyIndex], context);
        }
        else if (function.Parameters.Count > 0 && function.Parameters[0].Default is { } defaultNode)
        {
            EmitExpression(defaultNode, context);
        }
        else
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.Result))!);
    }

    private static void EmitHttpHeader(BoundNode node, FunctionSymbol function, EmitContext context)
    {
        var offset = HttpArgumentOffset(node, function);
        EmitHttpReceiver(node, offset, context);
        EmitHttpParameter(node, offset, 0, function, context);
        EmitHttpParameter(node, offset, 1, function, context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.Header))!);
    }

    private static void EmitHttpSend(BoundNode node, FunctionSymbol function, string method, EmitContext context)
    {
        var offset = HttpArgumentOffset(node, function);
        EmitHttpReceiver(node, offset, context);
        context.Il.Emit(OpCodes.Ldstr, method);
        EmitHttpParameter(node, offset, 0, function, context);
        if (function.Parameters.Count > 1)
        {
            EmitHttpParameter(node, offset, 1, function, context);
        }
        else
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }

        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.Send))!);
    }

    private static void EmitHttpJson(BoundNode node, EmitContext context)
    {
        if (node.Children.Length == 0)
        {
            context.Diagnostics.Add(Unsupported(node, "json yanıt ister."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        EmitExpression(node.Children[0], context);
        EmitJsonReadShape(node.Type, context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.Json))!);
    }

    private static int HttpArgumentOffset(BoundNode node, FunctionSymbol function)
    {
        if (node.Children.Length == 0)
        {
            return 0;
        }

        if (node.Children.Length > function.Parameters.Count && node.Children[0].Symbol is ModuleSymbol)
        {
            return 1;
        }

        if (node.Children[0].Symbol is ModuleSymbol
            || node.Children[0].Type is HttpBuilderTypeSymbol or HttpResponseTypeSymbol)
        {
            return 1;
        }

        return 0;
    }

    private static void EmitHttpReceiver(BoundNode node, int offset, EmitContext context)
    {
        if (offset == 1
            && node.Children.Length > 0
            && node.Children[0].Symbol is not ModuleSymbol)
        {
            EmitExpression(node.Children[0], context);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitHttpParameter(BoundNode node, int offset, int parameterIndex, FunctionSymbol function, EmitContext context)
    {
        var childIndex = offset + parameterIndex;
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

    private static void EmitRouteBootstrap(
        BoundCompilation compilation,
        ILGenerator il,
        Dictionary<FunctionSymbol, MethodBuilder> methods,
        TypeBuilder programType,
        List<Diagnostic> diagnostics)
    {
        il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.ResetRoutes))!);
        var wrappers = new Dictionary<RouteSymbol, MethodBuilder>();
        var index = 0;
        foreach (var route in compilation.Routes)
        {
            var handler = route.Handler.Template ?? route.Handler;
            if (!methods.ContainsKey(handler) && !methods.ContainsKey(route.Handler))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.EmitUnsupported,
                    DiagnosticSeverity.Error,
                    $"Rota işleyici '{route.Handler.Name}' emit edilmedi.",
                    null,
                    route.Span,
                    1,
                    1,
                    null));
                continue;
            }

            var wrapper = programType.DefineMethod(
                "UsRoute_" + index + "_" + Sanitize(route.Handler.Name),
                MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                handler.IsAsync ? typeof(Task<UsValue>) : typeof(UsValue),
                [typeof(UsValue[])]);
            wrappers[route] = wrapper;
            index++;
        }

        foreach (var route in compilation.Routes)
        {
            if (!wrappers.TryGetValue(route, out var wrapper))
            {
                continue;
            }

            EmitRouteWrapper(route, wrapper, methods);
            EmitRouteRegisterCall(route, wrapper, il);
        }
    }

    private static void EmitRouteWrapper(
        RouteSymbol route,
        MethodBuilder wrapper,
        Dictionary<FunctionSymbol, MethodBuilder> methods)
    {
        var handler = route.Handler.Template ?? route.Handler;
        if (!methods.TryGetValue(handler, out var target) && !methods.TryGetValue(route.Handler, out target))
        {
            var ilFail = wrapper.GetILGenerator();
            ilFail.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            if (handler.IsAsync)
            {
                ilFail.Emit(OpCodes.Call, typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(UsValue)));
            }

            ilFail.Emit(OpCodes.Ret);
            return;
        }

        var il = wrapper.GetILGenerator();
        for (var parameterIndex = 0; parameterIndex < handler.Parameters.Count; parameterIndex++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, parameterIndex);
            // UsValue is a struct — ldelem.ref is invalid (InvalidProgramException at JIT).
            il.Emit(OpCodes.Ldelem, typeof(UsValue));
        }

        il.Emit(OpCodes.Call, target);
        if (handler.ReturnType is UnitTypeSymbol)
        {
            if (handler.IsAsync)
            {
                // Task → Task<UsValue>
                il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.BoxUnitTask))!);
            }
            else
            {
                il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }
        }

        il.Emit(OpCodes.Ret);
    }

    private static void EmitRouteRegisterCall(RouteSymbol route, MethodBuilder wrapper, ILGenerator il)
    {
        var handler = route.Handler.Template ?? route.Handler;
        var parameterNames = handler.Parameters.Select(item => item.Name).ToArray();
        var parameterKinds = handler.Parameters.Select(parameter =>
        {
            if (route.Parameters.Contains(parameter.Name))
            {
                return "route";
            }

            if (IsModelType(parameter.Type))
            {
                return "body";
            }

            return "query";
        }).ToArray();
        var parameterTypeNames = handler.Parameters.Select(DescribeHttpParamType).ToArray();
        var bodyShapes = handler.Parameters.Select(parameter =>
            IsModelType(parameter.Type) ? EncodeModelShape(parameter.Type) : string.Empty).ToArray();
        var setupServices = route.Controller.Setup
            .Where(item => item.Constant == "servis" && item.Type is SourceTypeSymbol)
            .Select(item => ((SourceTypeSymbol)item.Type).Definition.Name)
            .ToArray();

        il.Emit(OpCodes.Ldstr, route.Method);
        il.Emit(OpCodes.Ldstr, route.Template);
        EmitStringArrayStatic(il, parameterNames);
        EmitStringArrayStatic(il, parameterKinds);
        EmitStringArrayStatic(il, parameterTypeNames);
        EmitStringArrayStatic(il, bodyShapes);
        EmitStringArrayStatic(il, setupServices);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, wrapper);
        if (handler.IsAsync)
        {
            il.Emit(OpCodes.Newobj, typeof(Func<UsValue[], Task<UsValue>>).GetConstructor([typeof(object), typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.RegisterAsync))!);
        }
        else
        {
            il.Emit(OpCodes.Newobj, typeof(Func<UsValue[], UsValue>).GetConstructor([typeof(object), typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.Register))!);
        }
    }

    private static bool IsModelType(TypeSymbol type)
    {
        var bare = type.WithNullable(false);
        return bare is SourceTypeSymbol source && source.Definition.TypeKind == SourceTypeKind.Model;
    }

    private static string DescribeHttpParamType(ParameterSymbol parameter)
    {
        var type = parameter.Type;
        var suffix = type.IsNullable ? "?" : string.Empty;
        var bare = type.WithNullable(false);
        if (bare is SourceTypeSymbol source)
        {
            return source.Definition.Name + suffix;
        }

        return bare.Name + suffix;
    }

    private static string EncodeModelShape(TypeSymbol type)
    {
        if (type.WithNullable(false) is not SourceTypeSymbol source)
        {
            return string.Empty;
        }

        var fields = CollectFields(source);
        return UsEmitJson.EncodeShape(
            source.Name,
            fields.Select(field => field.Name).ToArray(),
            fields.Select(DescribeFieldType).ToArray(),
            fields.Select(field => field.Type.IsNullable).ToArray(),
            fields.Select(field => field.IsRequired).ToArray(),
            fields.Select(EncodeDefaultJson).ToArray());
    }

    private static void EmitControllerInjections(
        FunctionSymbol function,
        BoundCompilation compilation,
        EmitContext context)
    {
        var route = compilation.Routes.FirstOrDefault(item =>
            ReferenceEquals(item.Handler, function)
            || ReferenceEquals(item.Handler.Template, function)
            || ReferenceEquals(item.Handler, function.Template));
        if (route is null)
        {
            return;
        }

        foreach (var setup in route.Controller.Setup)
        {
            if (setup.Symbol is not VariableSymbol variable)
            {
                continue;
            }

            EmitBindServiceOrDb(setup, variable, context);
        }
    }

    private static void EmitMaybeDispatchTestRoute(ILGenerator il)
    {
        il.Emit(OpCodes.Call, typeof(UsEmitHttp).GetMethod(nameof(UsEmitHttp.MaybeDispatchTestRoute))!);
        // Direct host call so Us.Hosting is an assemblyref of the emitted program (loads with AspNetCore).
        il.Emit(OpCodes.Call, typeof(UsKestrelHost).GetMethod(nameof(UsKestrelHost.MaybeListen))!);
    }
}
