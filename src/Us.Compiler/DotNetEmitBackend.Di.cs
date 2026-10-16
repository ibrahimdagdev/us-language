using System.Reflection;
using System.Reflection.Emit;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend
{
    private static Dictionary<string, MethodBuilder> DefineServiceFactories(
        BoundCompilation compilation,
        TypeBuilder type,
        Dictionary<FunctionSymbol, MethodBuilder> methods,
        List<Diagnostic> diagnostics)
    {
        var factories = new Dictionary<string, MethodBuilder>(StringComparer.Ordinal);
        var index = 0;
        foreach (var registration in compilation.Services)
        {
            var name = registration.ServiceType.Name;
            var method = type.DefineMethod(
                "UsSvc_" + index + "_" + Sanitize(name),
                MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                typeof(UsValue),
                Type.EmptyTypes);
            factories[name] = method;
            index++;
        }

        foreach (var registration in compilation.Services)
        {
            EmitServiceFactory(registration, factories[registration.ServiceType.Name], methods, type, diagnostics);
        }

        return factories;
    }

    private static void EmitServiceFactory(
        ServiceRegistration registration,
        MethodBuilder method,
        Dictionary<FunctionSymbol, MethodBuilder> methods,
        TypeBuilder programType,
        List<Diagnostic> diagnostics)
    {
        var il = method.GetILGenerator();
        var context = new EmitContext(il, methods, programType, returnsValue: true);
        var sourceType = registration.ServiceType;
        var fields = CollectFields(sourceType);

        il.Emit(OpCodes.Ldstr, sourceType.Name);
        il.Emit(OpCodes.Ldc_I4, fields.Length);
        il.Emit(OpCodes.Newarr, typeof(string));
        for (var index = 0; index < fields.Length; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldstr, fields[index].Name);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Ldc_I4, fields.Length);
        il.Emit(OpCodes.Newarr, typeof(bool));
        for (var index = 0; index < fields.Length; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(fields[index].Type.IsNullable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stelem_I1);
        }

        il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.Create))!);
        var instance = il.DeclareLocal(typeof(UsValue));
        il.Emit(OpCodes.Stloc, instance);

        foreach (var field in fields)
        {
            if (field.Default is not null)
            {
                il.Emit(OpCodes.Ldloc, instance);
                il.Emit(OpCodes.Ldstr, field.Name);
                EmitExpression(field.Default, context);
                il.Emit(OpCodes.Ldc_I4, registration.Span.Start);
                il.Emit(OpCodes.Ldc_I4, registration.Span.Length);
                il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
                continue;
            }

            if (sourceType.TypeKind == SourceTypeKind.Model)
            {
                continue;
            }

            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Ldstr, field.Name);
            if (!TryEmitZero(field.Type, context))
            {
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Pop);
                continue;
            }

            il.Emit(OpCodes.Ldc_I4, registration.Span.Start);
            il.Emit(OpCodes.Ldc_I4, registration.Span.Length);
            il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
        }

        if (registration.Constructor is { } constructor)
        {
            for (var index = 0; index < constructor.Parameters.Count; index++)
            {
                var parameter = constructor.Parameters[index];
                if (ServiceDependencyName(parameter.Type) is { } dependencyName)
                {
                    il.Emit(OpCodes.Ldstr, dependencyName);
                    il.Emit(OpCodes.Ldc_I4, registration.Span.Start);
                    il.Emit(OpCodes.Ldc_I4, registration.Span.Length);
                    il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.Resolve))!);
                }
                else if (parameter.Default is not null)
                {
                    EmitExpression(parameter.Default, context);
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.EmitUnsupported,
                        DiagnosticSeverity.Error,
                        $"'{parameter.Type.DisplayName}' kayıtlı değil.",
                        null,
                        registration.Span,
                        1,
                        1,
                        "Parity matrisi: docs/compiler-parity.md"));
                    il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                }

                var local = il.DeclareLocal(typeof(UsValue));
                il.Emit(OpCodes.Stloc, local);
                context.Locals[parameter] = local;
            }

            context.ThisLocal = instance;
            if (constructor.BoundBody is not null)
            {
                EmitConstructorBody(constructor.BoundBody, instance, context);
            }

            context.ThisLocal = null;
        }

        diagnostics.AddRange(context.Diagnostics);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ret);
    }

    private static string? ServiceDependencyName(TypeSymbol type) => type switch
    {
        SourceTypeSymbol source when source.Definition.ServiceLifetime is not null => source.Definition.Name,
        ConstructedTypeSymbol constructed when constructed.Definition is SourceTypeSymbol source
            && source.Definition.ServiceLifetime is not null => source.Definition.Name,
        _ => null,
    };

    private static void EmitDiDatabaseBootstrap(
        BoundCompilation compilation,
        ILGenerator il,
        Dictionary<string, MethodBuilder> factories)
    {
        il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.Reset))!);
        il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Reset))!);
        // Ensure Us.Data.SqlServer is loaded and factory/schema hooks are registered before env configure.
        il.Emit(OpCodes.Call, typeof(global::Us.Data.SqlServer.SqlServerRegistration).GetMethod(
            nameof(global::Us.Data.SqlServer.SqlServerRegistration.Register))!);
        il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.ConfigureFromEnvironment))!);

        foreach (var registration in compilation.Services)
        {
            if (!factories.TryGetValue(registration.ServiceType.Name, out var factory))
            {
                continue;
            }

            il.Emit(OpCodes.Ldstr, registration.ServiceType.Name);
            il.Emit(OpCodes.Ldc_I4, LifetimeCode(registration.Lifetime));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, factory);
            il.Emit(OpCodes.Newobj, typeof(Func<UsValue>).GetConstructor([typeof(object), typeof(IntPtr)])!);
            il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.Register))!);
        }

        foreach (var database in compilation.Databases)
        {
            foreach (var table in database.Tables)
            {
                EmitRegisterTable(il, table);
            }

            foreach (var table in database.Tables)
            {
                EmitRegisterRelations(il, table, database);
            }
        }

        // Additive SQL schema sync when a SQL connection env var is set (no-op for memory).
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.EnsureSqlSchema), [typeof(bool)])!);

        il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.BeginScope))!);
    }

    private static void EmitRegisterTable(ILGenerator il, TableSymbol table)
    {
        var model = table.Model as SourceTypeSymbol
            ?? throw new InvalidOperationException("Tablo modeli yok.");
        var fields = CollectFields(model.Definition);
        il.Emit(OpCodes.Ldstr, table.Name);
        il.Emit(OpCodes.Ldstr, model.Definition.Name);
        EmitStringArrayStatic(il, fields.Select(field => field.Name).ToArray());
        EmitStringArrayStatic(il, fields.Select(FieldTypeToken).ToArray());
        EmitBoolArrayStatic(il, fields.Select(field => field.Type.IsNullable).ToArray());
        EmitBoolArrayStatic(il, fields.Select(field => field.IsRequired).ToArray());
        var key = fields.FirstOrDefault(field => field.IsKey);
        if (key is null)
        {
            il.Emit(OpCodes.Ldnull);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, key.Name);
        }

        il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(
            nameof(UsEmitDatabase.RegisterTable),
            [typeof(string), typeof(string), typeof(string[]), typeof(string[]), typeof(bool[]), typeof(bool[]), typeof(string)])!);
    }

    private static string FieldTypeToken(FieldSymbol field)
    {
        var type = field.Type.WithNullable(false);
        if (type is SourceTypeSymbol source && source.Definition.TypeKind == SourceTypeKind.Model)
        {
            return "model:" + source.Definition.Name;
        }

        return type.DisplayName;
    }

    private static void EmitRegisterRelations(ILGenerator il, TableSymbol table, DatabaseSymbol database)
    {
        var model = table.Model as SourceTypeSymbol
            ?? throw new InvalidOperationException("Tablo modeli yok.");
        foreach (var field in CollectFields(model.Definition))
        {
            if (field.Type.WithNullable(false) is not SourceTypeSymbol related
                || related.Definition.TypeKind != SourceTypeKind.Model)
            {
                continue;
            }

            var relatedTable = database.Tables.FirstOrDefault(candidate =>
                candidate.Model is SourceTypeSymbol candidateModel
                && ReferenceEquals(candidateModel.Definition, related.Definition));
            if (relatedTable is null)
            {
                continue;
            }

            var relatedModel = relatedTable.Model as SourceTypeSymbol
                ?? throw new InvalidOperationException("İlişkili tablo modeli yok.");
            var key = CollectFields(relatedModel.Definition).FirstOrDefault(item => item.IsKey);
            if (key is null)
            {
                continue;
            }

            il.Emit(OpCodes.Ldstr, table.Name);
            il.Emit(OpCodes.Ldstr, field.Name);
            il.Emit(OpCodes.Ldstr, relatedTable.Name);
            il.Emit(OpCodes.Ldstr, key.Name);
            il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.RegisterRelation))!);
        }
    }

    private static void EmitStringArrayStatic(ILGenerator il, string[] values)
    {
        il.Emit(OpCodes.Ldc_I4, values.Length);
        il.Emit(OpCodes.Newarr, typeof(string));
        for (var index = 0; index < values.Length; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldstr, values[index]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private static void EmitBoolArrayStatic(ILGenerator il, bool[] values)
    {
        il.Emit(OpCodes.Ldc_I4, values.Length);
        il.Emit(OpCodes.Newarr, typeof(bool));
        for (var index = 0; index < values.Length; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(values[index] ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stelem_I1);
        }
    }

    private static int LifetimeCode(ServiceLifetime lifetime) => lifetime switch
    {
        ServiceLifetime.Scoped => UsEmitDi.LifetimeScoped,
        ServiceLifetime.Singleton => UsEmitDi.LifetimeSingleton,
        _ => UsEmitDi.LifetimeTransient,
    };

    private static void EmitEndScope(ILGenerator il)
    {
        il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.EndScope))!);
    }

    private static void EmitBindServiceOrDb(BoundNode node, VariableSymbol variable, EmitContext context)
    {
        if (context.IsAsyncMoveNext)
        {
            context.AllocateSlot(variable);
        }
        else
        {
            var local = context.Il.DeclareLocal(typeof(UsValue));
            context.Locals[variable] = local;
        }

        if (node.Constant == "db")
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Bind))!);
        }
        else if (node.Constant == "servis" && node.Type is SourceTypeSymbol serviceType)
        {
            context.Il.Emit(OpCodes.Ldstr, serviceType.Definition.Name);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitDi).GetMethod(nameof(UsEmitDi.Resolve))!);
        }
        else
        {
            context.Diagnostics.Add(Unsupported(node, "bağla servis/db çözülemedi."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }

        if (context.IsAsyncMoveNext)
        {
            EmitStoreLocal(variable, context);
        }
        else
        {
            context.Il.Emit(OpCodes.Stloc, context.Locals[variable]);
        }
    }

    private static void EmitTransaction(BoundNode node, EmitContext context)
    {
        context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Begin))!);
        context.Il.BeginExceptionBlock();
        context.TryDepth++;
        foreach (var child in node.Children)
        {
            EmitStatement(child, context);
        }

        context.Il.Emit(OpCodes.Ldc_I4_1);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Finish))!);

        context.Il.BeginCatchBlock(typeof(Exception));
        context.Il.Emit(OpCodes.Pop);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Finish))!);
        context.Il.Emit(OpCodes.Rethrow);
        context.Il.EndExceptionBlock();
        context.TryDepth--;
    }

    private static void EmitTransactionTerminal(BoundNode node, EmitContext context)
    {
        if (node.Operator == TokenKind.Onayla)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Commit))!);
            return;
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Rollback))!);
    }

    private static bool TryEmitTableMember(BoundNode node, EmitContext context)
    {
        if (node.Symbol is not TableSymbol table)
        {
            return false;
        }

        context.Il.Emit(OpCodes.Ldstr, table.Name);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.OpenQuery))!);
        return true;
    }

    private static bool TryEmitTableQueryCall(BoundNode node, EmitContext context)
    {
        if (node.Children.Length == 0 || node.Children[0].Type is not TableQueryTypeSymbol)
        {
            return false;
        }

        var name = node.Constant ?? string.Empty;
        EmitExpression(node.Children[0], context);
        switch (name)
        {
            case "filtrele" when node.Children.Length >= 2:
                if (TryEmitQueryExpr(node.Children[1], context))
                {
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.FilterExpr))!);
                    return true;
                }

                EmitPredicateDelegate(node.Children[1], context, typeof(Func<UsValue, bool>));
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Filter))!);
                return true;
            case "sırala" when node.Children.Length >= 2:
                if (TryEmitSortField(node.Children[1], context))
                {
                    context.Il.Emit(OpCodes.Ldc_I4_0);
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.SortByField))!);
                    return true;
                }

                EmitPredicateDelegate(node.Children[1], context, typeof(Func<UsValue, UsValue>));
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Sort))!);
                return true;
            case "tsırala" when node.Children.Length >= 2:
                if (TryEmitSortField(node.Children[1], context))
                {
                    context.Il.Emit(OpCodes.Ldc_I4_1);
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                    context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.SortByField))!);
                    return true;
                }

                EmitPredicateDelegate(node.Children[1], context, typeof(Func<UsValue, UsValue>));
                context.Il.Emit(OpCodes.Ldc_I4_1);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Sort))!);
                return true;
            case "birlikte" when node.Children.Length >= 2
                && node.Children[1].Symbol is FieldSymbol includeField:
                context.Il.Emit(OpCodes.Ldstr, includeField.Name);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Include))!);
                return true;
            case "tümü":
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.All))!);
                return true;
            case "ekle" when node.Children.Length >= 2:
                EmitExpression(node.Children[1], context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Insert))!);
                return true;
            case "güncelle" when node.Children.Length >= 2:
                EmitExpression(node.Children[1], context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Update))!);
                return true;
            case "sil" when node.Children.Length >= 2:
                EmitExpression(node.Children[1], context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Delete))!);
                return true;
            default:
                context.Diagnostics.Add(Unsupported(node, $"Tablo sorgu '{name}' henüz derlenmiyor."));
                context.Il.Emit(OpCodes.Pop);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return true;
        }
    }

    private static bool TryEmitSortField(BoundNode node, EmitContext context)
    {
        if (!IsCaretField(node, out var field))
        {
            return false;
        }

        context.Il.Emit(OpCodes.Ldstr, field.Name);
        return true;
    }

    private static bool TryEmitQueryExpr(BoundNode node, EmitContext context)
    {
        if (!IsStructuredQueryExpr(node))
        {
            return false;
        }

        EmitQueryExpr(node, context);
        return true;
    }

    private static bool IsStructuredQueryExpr(BoundNode node)
    {
        if (IsCaretField(node, out _))
        {
            return true;
        }

        if (node.Kind == BoundKind.Literal)
        {
            return true;
        }

        if (node.Kind == BoundKind.Unary && node.Operator == TokenKind.Bang && node.Children.Length == 1)
        {
            return IsStructuredQueryExpr(node.Children[0]);
        }

        if (node.Kind == BoundKind.Binary && node.Children.Length == 2)
        {
            return node.Operator is TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Greater or TokenKind.GreaterEqual
                or TokenKind.Less or TokenKind.LessEqual or TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                && IsStructuredQueryExpr(node.Children[0])
                && IsStructuredQueryExpr(node.Children[1]);
        }

        return false;
    }

    private static bool IsCaretField(BoundNode node, out FieldSymbol field)
    {
        field = null!;
        if (node.Kind == BoundKind.Member
            && node.Symbol is FieldSymbol member
            && node.Children.Length > 0
            && node.Children[0].Operator == TokenKind.Caret)
        {
            field = member;
            return true;
        }

        return false;
    }

    private static void EmitQueryExpr(BoundNode node, EmitContext context)
    {
        if (IsCaretField(node, out var field))
        {
            // ^.field alone in filter context → Truthy(Field)
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Field))!);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Truthy))!);
            return;
        }

        if (node.Kind == BoundKind.Literal)
        {
            EmitLiteral(node, context);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Constant), [typeof(UsValue)])!);
            return;
        }

        if (node.Kind == BoundKind.Unary && node.Operator == TokenKind.Bang)
        {
            EmitQueryExprOperand(node.Children[0], context);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Not))!);
            return;
        }

        if (node.Kind == BoundKind.Binary && node.Children.Length == 2)
        {
            if (node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
            {
                EmitQueryExpr(node.Children[0], context);
                EmitQueryExpr(node.Children[1], context);
                var method = node.Operator == TokenKind.AmpersandAmpersand
                    ? nameof(UsQueryExpr.And)
                    : nameof(UsQueryExpr.Or);
                context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(method)!);
                return;
            }

            EmitQueryExprOperand(node.Children[0], context);
            context.Il.Emit(OpCodes.Ldstr, OpToken(node.Operator));
            EmitQueryExprOperand(node.Children[1], context);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Compare))!);
            return;
        }

        throw new InvalidOperationException("Yapılandırılmış sorgu ifadesi bekleniyordu.");
    }

    private static void EmitQueryExprOperand(BoundNode node, EmitContext context)
    {
        if (IsCaretField(node, out var field))
        {
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Field))!);
            return;
        }

        if (node.Kind == BoundKind.Literal)
        {
            EmitLiteral(node, context);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Constant), [typeof(UsValue)])!);
            return;
        }

        if (node.Kind == BoundKind.Unary && node.Operator == TokenKind.Bang)
        {
            EmitQueryExprOperand(node.Children[0], context);
            context.Il.Emit(OpCodes.Call, typeof(UsQueryExpr).GetMethod(nameof(UsQueryExpr.Not))!);
            return;
        }

        if (node.Kind == BoundKind.Binary && node.Children.Length == 2
            && node.Operator is TokenKind.AmpersandAmpersand or TokenKind.PipePipe
                or TokenKind.EqualEqual or TokenKind.BangEqual or TokenKind.Greater or TokenKind.GreaterEqual
                or TokenKind.Less or TokenKind.LessEqual)
        {
            EmitQueryExpr(node, context);
            return;
        }

        throw new InvalidOperationException("Yapılandırılmış sorgu operandı bekleniyordu.");
    }

    private static string OpToken(TokenKind? op) => op switch
    {
        TokenKind.EqualEqual => "==",
        TokenKind.BangEqual => "!=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        _ => throw new InvalidOperationException(op?.ToString() ?? "op"),
    };
}
