using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Us.Core;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;
using Task = System.Threading.Tasks.Task;

namespace Us.Compiler;

public sealed partial class DotNetEmitBackend : IBackend
{
    public const string AssemblyFileName = "UsProgram.dll";

    public BackendResult Emit(BoundCompilation compilation, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        outputDirectory ??= Path.Combine(Path.GetTempPath(), "us-emit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var walk = BoundTreeWalker.Walk(compilation);
        if (walk.HasErrors)
        {
            return BackendResult.Fail([.. walk.Diagnostics]);
        }

        try
        {
            var assemblyName = new AssemblyName("UsProgram");
            var ab = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var module = ab.DefineDynamicModule("UsProgram");
            var type = module.DefineType("Us.Program", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit);
            var methods = new Dictionary<FunctionSymbol, MethodBuilder>();
            var moveNextMethods = new Dictionary<FunctionSymbol, MethodBuilder>();
            var diagnostics = new List<Diagnostic>();
            var freeFunctions = compilation.Functions
                .Where(item => item.Intrinsic == Intrinsic.None
                    && item.BoundBody is not null
                    && !item.IsConstructor
                    && item.DeclaringType is null
                    && !item.IsAbstract)
                .ToArray();
            // Include async instance methods (M23). Sync-only filter removed; ByRef+async still gated.
            var instanceMethods = compilation.Functions
                .Where(item => item.Intrinsic == Intrinsic.None
                    && item.BoundBody is not null
                    && !item.IsConstructor
                    && item.DeclaringType is not null
                    && !item.IsStatic
                    && !item.IsAbstract)
                .ToArray();
            for (var index = 0; index < freeFunctions.Length; index++)
            {
                var function = freeFunctions[index];
                var returnsValue = function.ReturnType is not UnitTypeSymbol;
                if (function.IsAsync && HasByRefParameters(function))
                {
                    diagnostics.Add(AsyncByRefUnsupported(function, function.BoundBody!.Span));
                    continue;
                }

                var parameterTypes = ClrParameterTypes(function, instanceReceiver: false);
                if (function.IsAsync)
                {
                    var method = type.DefineMethod(
                        "UsFn_" + index + "_" + Sanitize(function.Name),
                        MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                        returnsValue ? typeof(Task<UsValue>) : typeof(Task),
                        parameterTypes);
                    ApplyByRefParameterAttributes(method, function, instanceReceiver: false);
                    methods[function] = method;
                    moveNextMethods[function] = DefineAsyncMoveNext(type, "UsFn_" + index + "_" + Sanitize(function.Name) + "_MoveNext");
                }
                else
                {
                    var method = type.DefineMethod(
                        "UsFn_" + index + "_" + Sanitize(function.Name),
                        MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                        returnsValue ? typeof(UsValue) : typeof(void),
                        parameterTypes);
                    ApplyByRefParameterAttributes(method, function, instanceReceiver: false);
                    methods[function] = method;
                }
            }

            for (var index = 0; index < instanceMethods.Length; index++)
            {
                var function = instanceMethods[index];
                var returnsValue = function.ReturnType is not UnitTypeSymbol;
                if (function.IsAsync && HasByRefParameters(function))
                {
                    diagnostics.Add(AsyncByRefUnsupported(function, function.BoundBody!.Span));
                    continue;
                }

                // CLR signature: (UsValue self, UsValue/UsValue&... args)
                var parameterTypes = ClrParameterTypes(function, instanceReceiver: true);
                var name = "UsIm_" + index + "_" + Sanitize(function.DeclaringType!.Name) + "_" + Sanitize(function.Name);
                if (function.IsAsync)
                {
                    var method = type.DefineMethod(
                        name,
                        MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                        returnsValue ? typeof(Task<UsValue>) : typeof(Task),
                        parameterTypes);
                    ApplyByRefParameterAttributes(method, function, instanceReceiver: true);
                    methods[function] = method;
                    moveNextMethods[function] = DefineAsyncMoveNext(type, name + "_MoveNext");
                }
                else
                {
                    var method = type.DefineMethod(
                        name,
                        MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                        returnsValue ? typeof(UsValue) : typeof(void),
                        parameterTypes);
                    ApplyByRefParameterAttributes(method, function, instanceReceiver: true);
                    methods[function] = method;
                }
            }

            var asyncMain = TopLevelNeedsAsync(compilation);
            var main = type.DefineMethod(
                "Main",
                MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                Type.EmptyTypes);
            MethodBuilder? mainMoveNext = null;
            if (asyncMain)
            {
                mainMoveNext = DefineAsyncMoveNext(type, "Main_MoveNext");
            }
            var serviceFactories = DefineServiceFactories(compilation, type, methods, diagnostics);
            var mainIl = main.GetILGenerator();
            EmitUtf8Console(mainIl);
            EmitBootstrap(mainIl);
            EmitDiDatabaseBootstrap(compilation, mainIl, serviceFactories);
            EmitRouteBootstrap(compilation, mainIl, methods, type, diagnostics);
            var vtableSerial = 0;
            foreach (var function in instanceMethods)
            {
                if (function.IsAsync || !methods.ContainsKey(function))
                {
                    continue;
                }

                EmitVTableRegistration(mainIl, function, methods[function], type, ref vtableSerial);
            }

            if (asyncMain)
            {
                // void Main → Task (async state machine) → RunEntry bridge (PE requires void Main).
                mainIl.BeginExceptionBlock();
                mainIl.Emit(OpCodes.Ldnull);
                mainIl.Emit(OpCodes.Ldftn, mainMoveNext!);
                mainIl.Emit(OpCodes.Newobj, typeof(Action<UsAsyncBox>).GetConstructor([typeof(object), typeof(IntPtr)])!);
                mainIl.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Start), [typeof(Action<UsAsyncBox>)])!);
                mainIl.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.RunEntry))!);
                EmitMaybeDispatchTestRoute(mainIl);
                mainIl.BeginFinallyBlock();
                EmitEndScope(mainIl);
                mainIl.EndExceptionBlock();
                mainIl.Emit(OpCodes.Ret);
                EmitAsyncMoveNextBody(
                    mainMoveNext!,
                    compilation.Members,
                    methods,
                    type,
                    returnsValue: false,
                    diagnostics);
            }
            else
            {
                var mainContext = new EmitContext(mainIl, methods, type, returnsValue: false)
                {
                    LambdaSerial = vtableSerial,
                };
                mainIl.BeginExceptionBlock();
                foreach (var member in compilation.Members)
                {
                    EmitStatement(member, mainContext);
                }

                diagnostics.AddRange(mainContext.Diagnostics);
                EmitMaybeDispatchTestRoute(mainIl);
                mainIl.BeginFinallyBlock();
                EmitEndScope(mainIl);
                mainIl.EndExceptionBlock();
                mainIl.Emit(OpCodes.Ret);
            }

            foreach (var function in freeFunctions)
            {
                if (!methods.TryGetValue(function, out var method))
                {
                    continue;
                }

                var returnsValue = function.ReturnType is not UnitTypeSymbol;
                if (function.IsAsync)
                {
                    var entryIl = method.GetILGenerator();
                    // Stash parameters into a temporary box seed via MoveNext capturing args in slots.
                    // Entry: store args into static thread-local? Better: MoveNext reads args from a closure frame.
                    // Pass parameters as UsAsyncBox slots 0..n-1 by a trampoline:
                    EmitAsyncFunctionTrampoline(entryIl, function, moveNextMethods[function], returnsValue);
                    EmitAsyncMoveNextBody(
                        moveNextMethods[function],
                        [function.BoundBody!],
                        methods,
                        type,
                        returnsValue,
                        diagnostics,
                        context =>
                        {
                            for (var parameterIndex = 0; parameterIndex < function.Parameters.Count; parameterIndex++)
                            {
                                context.AllocateSlot(function.Parameters[parameterIndex]);
                            }
                        },
                        context => EmitControllerInjections(function, compilation, context));
                }
                else
                {
                    var bodyIl = method.GetILGenerator();
                    var context = new EmitContext(bodyIl, methods, type, returnsValue: returnsValue);
                    BindByRefParameters(function, context, argumentOffset: 0);
                    EmitValueParameterProlog(function, bodyIl, context, argumentOffset: 0);
                    EmitControllerInjections(function, compilation, context);
                    EmitStatement(function.BoundBody!, context);
                    diagnostics.AddRange(context.Diagnostics);
                    if (function.ReturnType is UnitTypeSymbol)
                    {
                        bodyIl.Emit(OpCodes.Ret);
                    }
                    else
                    {
                        bodyIl.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                        bodyIl.Emit(OpCodes.Ret);
                    }
                }
            }

            foreach (var function in instanceMethods)
            {
                if (!methods.TryGetValue(function, out var method))
                {
                    continue;
                }

                var returnsValue = function.ReturnType is not UnitTypeSymbol;
                if (function.IsAsync)
                {
                    var entryIl = method.GetILGenerator();
                    EmitAsyncInstanceTrampoline(entryIl, function, moveNextMethods[function], returnsValue);
                    EmitAsyncMoveNextBody(
                        moveNextMethods[function],
                        [function.BoundBody!],
                        methods,
                        type,
                        returnsValue,
                        diagnostics,
                        context =>
                        {
                            // Slot 0 = receiver (bu); parameters follow — matches trampoline layout.
                            context.ThisSlot = context.NextAsyncSlot++;
                            for (var parameterIndex = 0; parameterIndex < function.Parameters.Count; parameterIndex++)
                            {
                                context.AllocateSlot(function.Parameters[parameterIndex]);
                            }
                        });
                }
                else
                {
                    var bodyIl = method.GetILGenerator();
                    var context = new EmitContext(bodyIl, methods, type, returnsValue: returnsValue);
                    var selfLocal = bodyIl.DeclareLocal(typeof(UsValue));
                    bodyIl.Emit(OpCodes.Ldarg_0);
                    bodyIl.Emit(OpCodes.Stloc, selfLocal);
                    context.ThisLocal = selfLocal;
                    BindByRefParameters(function, context, argumentOffset: 1);
                    EmitValueParameterProlog(function, bodyIl, context, argumentOffset: 1);
                    EmitStatement(function.BoundBody!, context);
                    diagnostics.AddRange(context.Diagnostics);
                    if (function.ReturnType is UnitTypeSymbol)
                    {
                        bodyIl.Emit(OpCodes.Ret);
                    }
                    else
                    {
                        bodyIl.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                        bodyIl.Emit(OpCodes.Ret);
                    }
                }
            }

            if (diagnostics.Count > 0)
            {
                return BackendResult.Fail([.. diagnostics]);
            }

            type.CreateType();

            var metadataBuilder = ab.GenerateMetadata(out var ilStream, out var fieldData);
            var peBuilder = new ManagedPEBuilder(
                header: PEHeaderBuilder.CreateExecutableHeader(),
                metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                ilStream: ilStream,
                mappedFieldData: fieldData,
                entryPoint: MetadataTokens.MethodDefinitionHandle(main.MetadataToken));
            var peBlob = new BlobBuilder();
            peBuilder.Serialize(peBlob);

            var artifact = Path.Combine(outputDirectory, AssemblyFileName);
            using (var stream = new FileStream(artifact, FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(stream);
            }

            WriteRuntimeConfig(Path.ChangeExtension(artifact, ".runtimeconfig.json"));
            CopyRuntimeAssemblies(outputDirectory);
            return BackendResult.Ok(artifact);
        }
        catch (Exception exception)
        {
            return BackendResult.Fail(new Diagnostic(
                DiagnosticCodes.EmitFailed,
                DiagnosticSeverity.Error,
                $"Derleme çıktısı üretilemedi: {exception.Message}",
                outputDirectory,
                new TextSpan(0, 0),
                1,
                1,
                null));
        }
    }

    private static void CopyRuntimeAssemblies(string outputDirectory)
    {
        // Copy Us.* runtime graph (Core/Runtime/…) from the Us.Runtime output directory.
        var runtimePath = typeof(UsValue).Assembly.Location;
        CopyBesideRuntime(outputDirectory, runtimePath);
        var runtimeDirectory = Path.GetDirectoryName(runtimePath);
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(runtimeDirectory, "Us.*.dll"))
            {
                CopyBesideRuntime(outputDirectory, file);
            }
        }

        // Kestrel host adapter (loaded by UsEmitHttp.MaybeListen when routes exist).
        CopyBesideRuntime(outputDirectory, typeof(global::Us.Hosting.UsKestrelHost).Assembly.Location);
        // SQL Server provider + SqlClient native/managed deps (activated when connection env is set).
        CopySqlServerProvider(outputDirectory);
    }

    private static void CopySqlServerProvider(string outputDirectory)
    {
        CopyBesideRuntime(outputDirectory, typeof(global::Us.Data.SqlServer.SqlServerRegistration).Assembly.Location);

        // Managed deps come from the running host output (CLI / test bin), not NuGet runtimes folder.
        foreach (var hostDirectory in SqlClientDependencyProbeDirectories())
        {
            foreach (var file in Directory.EnumerateFiles(hostDirectory, "*.dll"))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("Us.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith("Microsoft.", StringComparison.Ordinal)
                    || name.StartsWith("Azure.", StringComparison.Ordinal)
                    || name.StartsWith("System.IdentityModel", StringComparison.Ordinal)
                    || name.StartsWith("System.Memory.Data", StringComparison.Ordinal)
                    || name.StartsWith("System.Configuration", StringComparison.Ordinal)
                    || name.StartsWith("System.Security.Cryptography.ProtectedData", StringComparison.Ordinal)
                    || name.StartsWith("System.ClientModel", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.Bcl.", StringComparison.Ordinal)
                    || name.StartsWith("Microsoft.SqlServer.", StringComparison.Ordinal))
                {
                    // Skip portable SqlClient stub — replaced by win runtime asset below.
                    if (string.Equals(name, "Microsoft.Data.SqlClient.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    CopyBesideRuntime(outputDirectory, file);
                }
            }
        }

        CopyWinSqlClient(outputDirectory);
        CopySqlClientNative(outputDirectory, AppContext.BaseDirectory);
    }

    private static IEnumerable<string> SqlClientDependencyProbeDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     AppContext.BaseDirectory,
                     Path.GetDirectoryName(typeof(global::Us.Data.SqlServer.SqlServerRegistration).Assembly.Location),
                     Path.GetDirectoryName(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location),
                 })
        {
            if (string.IsNullOrEmpty(candidate) || !Directory.Exists(candidate) || !seen.Add(candidate))
            {
                continue;
            }

            yield return candidate;
        }
    }

    private static void CopyWinSqlClient(string outputDirectory)
    {
        var nuget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "microsoft.data.sqlclient");
        if (!Directory.Exists(nuget))
        {
            // Fall back to whatever the host loaded.
            CopyBesideRuntime(outputDirectory, typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
            return;
        }

        // Prefer Windows runtime asset over portable stub (stub throws PlatformNotSupportedException).
        foreach (var tfm in new[] { "net10.0", "net9.0", "net8.0" })
        {
            foreach (var versionDir in Directory.EnumerateDirectories(nuget).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(versionDir, "runtimes", "win", "lib", tfm, "Microsoft.Data.SqlClient.dll");
                if (File.Exists(candidate))
                {
                    CopyBesideRuntime(outputDirectory, candidate);
                    return;
                }
            }
        }

        CopyBesideRuntime(outputDirectory, typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.Location);
    }

    private static void CopySqlClientNative(string outputDirectory, string? sqlClientDirectory)
    {
        var nuget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "microsoft.data.sqlclient.sni.runtime");
        if (Directory.Exists(nuget))
        {
            foreach (var native in Directory.EnumerateFiles(nuget, "Microsoft.Data.SqlClient.SNI.dll", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}win-x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/win-x64/", StringComparison.OrdinalIgnoreCase)))
            {
                CopyBesideRuntime(outputDirectory, native);
                return;
            }
        }

        string[] probeRoots =
        [
            sqlClientDirectory ?? string.Empty,
            Path.Combine(sqlClientDirectory ?? string.Empty, "runtimes", "win-x64", "native"),
            Path.Combine(sqlClientDirectory ?? string.Empty, "runtimes", "win", "native"),
        ];
        foreach (var root in probeRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "Microsoft.Data.SqlClient.SNI*.dll"))
            {
                CopyBesideRuntime(outputDirectory, file);
                return;
            }
        }
    }

    private static void CopyBesideRuntime(string outputDirectory, string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, Path.Combine(outputDirectory, Path.GetFileName(sourcePath)), overwrite: true);
    }

    private static void WriteRuntimeConfig(string path)
    {
        // AspNetCore.App required when Us.Runtime hosts Kestrel (UsKestrelHost).
        const string json =
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  {
                    "name": "Microsoft.NETCore.App",
                    "version": "10.0.0"
                  },
                  {
                    "name": "Microsoft.AspNetCore.App",
                    "version": "10.0.0"
                  }
                ]
              }
            }
            """;
        File.WriteAllText(path, json.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void EmitUtf8Console(ILGenerator il)
    {
        var utf8 = typeof(Encoding).GetProperty(nameof(Encoding.UTF8))!.GetMethod!;
        var setOutput = typeof(Console).GetProperty(nameof(Console.OutputEncoding))!.GetSetMethod()!;
        var setInput = typeof(Console).GetProperty(nameof(Console.InputEncoding))!.GetSetMethod()!;
        il.Emit(OpCodes.Call, utf8);
        il.Emit(OpCodes.Call, setOutput);
        il.Emit(OpCodes.Call, utf8);
        il.Emit(OpCodes.Call, setInput);
    }

    private sealed class EmitContext
    {
        public EmitContext(ILGenerator il, Dictionary<FunctionSymbol, MethodBuilder> methods, TypeBuilder programType, bool returnsValue, bool isAsyncMoveNext = false)
        {
            Il = il;
            Methods = methods;
            ProgramType = programType;
            ReturnsValue = returnsValue;
            IsAsyncMoveNext = isAsyncMoveNext;
        }

        public ILGenerator Il { get; }

        public Dictionary<FunctionSymbol, MethodBuilder> Methods { get; }

        public TypeBuilder ProgramType { get; }

        public bool ReturnsValue { get; }

        public bool IsAsyncMoveNext { get; }

        public Dictionary<Symbol, LocalBuilder> Locals { get; } = new();

        /// <summary>CLR argument index for ref/çıktı parameters (managed pointer; not copied into Locals).</summary>
        public Dictionary<Symbol, int> ByRefArgIndexes { get; } = new();

        public Dictionary<Symbol, int> AsyncSlots { get; } = new();

        public int NextAsyncSlot { get; set; }

        public int NextAwaitState { get; set; } = 1;

        public Label[]? AwaitResumeLabels { get; set; }

        public Label AsyncExitLabel { get; set; }

        public List<Diagnostic> Diagnostics { get; } = [];

        public Stack<(Label Break, Label Continue)> Loops { get; } = new();

        public LocalBuilder? Caret { get; set; }

        public int? CaretSlot { get; set; }

        public LocalBuilder? ThisLocal { get; set; }

        public int? ThisSlot { get; set; }

        public int LambdaSerial { get; set; }

        public int TryDepth { get; set; }

        public int AllocateSlot(Symbol symbol)
        {
            if (AsyncSlots.TryGetValue(symbol, out var existing))
            {
                return existing;
            }

            var slot = NextAsyncSlot++;
            AsyncSlots[symbol] = slot;
            return slot;
        }
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.Length == 0 ? "fn" : builder.ToString();
    }

    private static void EmitConvertForParameter(TypeSymbol type, ParameterSymbol parameter, ILGenerator il, EmitContext context)
    {
        // Value already on stack from Ldarg.
        if (!ClrTypeMap.TryMapPrimitive(type, out _, out var kind))
        {
            return;
        }

        il.Emit(OpCodes.Ldc_I4, (int)kind);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Convert))!);
        il.Emit(type.IsNullable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Ldc_I4, 0);
        il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Store))!);
    }

    private static void EmitStatement(BoundNode node, EmitContext context)
    {
        switch (node.Kind)
        {
            case BoundKind.Block when node.Operator == TokenKind.Eesislem:
                EmitConcurrent(node, context);
                break;
            case BoundKind.Block when node.Operator == TokenKind.Islem:
                EmitTransaction(node, context);
                break;
            case BoundKind.Block:
                foreach (var child in node.Children)
                {
                    EmitStatement(child, context);
                }

                break;
            case BoundKind.Local when node.Symbol is null:
                foreach (var child in node.Children)
                {
                    EmitStatement(child, context);
                }

                break;
            case BoundKind.Local when node.Symbol is VariableSymbol variable
                && node.Constant is "servis" or "db":
                EmitBindServiceOrDb(node, variable, context);
                break;
            case BoundKind.Local when node.Symbol is VariableSymbol variable:
                EmitLocal(node, variable, context);
                break;
            case BoundKind.Expression when node.Operator == TokenKind.Firlat:
                EmitThrow(node, context);
                break;
            case BoundKind.Expression when node.Children.Length == 1:
                EmitExpression(node.Children[0], context);
                context.Il.Emit(OpCodes.Pop);
                break;
            case BoundKind.Call:
                EmitExpression(node, context);
                context.Il.Emit(OpCodes.Pop);
                break;
            case BoundKind.Assignment:
                EmitExpression(node, context);
                context.Il.Emit(OpCodes.Pop);
                break;
            case BoundKind.If when node.Operator == TokenKind.EqualsGreater:
                EmitExpression(node, context);
                context.Il.Emit(OpCodes.Pop);
                break;
            case BoundKind.If:
                EmitIf(node, context);
                break;
            case BoundKind.Loop:
                EmitLoop(node, context);
                break;
            case BoundKind.Jump:
                EmitJump(node, context);
                break;
            case BoundKind.Result:
                EmitResult(node, context);
                break;
            case BoundKind.Other when node.Operator == TokenKind.Dene:
                EmitTry(node, context);
                break;
            case BoundKind.Other when node.Constant == "ayar":
                EmitBindSetting(node, context);
                break;
            case BoundKind.Other when node.Operator is TokenKind.Onayla or TokenKind.Gerial:
                EmitTransactionTerminal(node, context);
                break;
            case BoundKind.Type:
            case BoundKind.Other when node.Operator is null && node.Children.Length == 0:
                break;
            case BoundKind.Other when IsDeclarationOnly(node):
                break;
            default:
                context.Diagnostics.Add(Unsupported(node));
                break;
        }
    }

    private static void EmitThrow(BoundNode node, EmitContext context)
    {
        EmitExpression(node.Children[0], context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Throw))!);
    }

    private static void EmitTry(BoundNode node, EmitContext context)
    {
        var body = node.Children[1];
        var finalizer = node.Children[2];
        var catches = node.Children[3];

        context.Il.BeginExceptionBlock();
        context.TryDepth++;
        EmitStatement(body, context);

        context.Il.BeginCatchBlock(typeof(Exception));
        var ex = context.Il.DeclareLocal(typeof(Exception));
        context.Il.Emit(OpCodes.Stloc, ex);
        var handled = context.Il.DefineLabel();
        foreach (var clause in catches.Children)
        {
            var next = context.Il.DefineLabel();
            context.Il.Emit(OpCodes.Ldloc, ex);
            if (clause.Flag)
            {
                context.Il.Emit(OpCodes.Ldnull);
            }
            else
            {
                context.Il.Emit(OpCodes.Ldstr, clause.Type.WithNullable(false).Name);
            }

            context.Il.Emit(clause.Flag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.CatchMatches))!);
            context.Il.Emit(OpCodes.Brfalse, next);
            if (clause.Symbol is not null)
            {
                context.Il.Emit(OpCodes.Ldloc, ex);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.ErrorFromException))!);
                if (context.IsAsyncMoveNext)
                {
                    EmitStoreLocal(clause.Symbol, context);
                }
                else
                {
                    var local = context.Il.DeclareLocal(typeof(UsValue));
                    context.Locals[clause.Symbol] = local;
                    context.Il.Emit(OpCodes.Stloc, local);
                }
            }

            if (clause.Children.Length > 0)
            {
                EmitStatement(clause.Children[0], context);
            }

            if (clause.Symbol is not null && !context.IsAsyncMoveNext)
            {
                context.Locals.Remove(clause.Symbol);
            }

            context.Il.Emit(OpCodes.Br, handled);
            context.Il.MarkLabel(next);
        }

        context.Il.Emit(OpCodes.Ldloc, ex);
        context.Il.Emit(OpCodes.Throw);
        context.Il.MarkLabel(handled);

        context.Il.BeginFinallyBlock();
        EmitStatement(finalizer, context);
        context.Il.EndExceptionBlock();
        context.TryDepth--;
    }

    private static bool IsDeclarationOnly(BoundNode node) =>
        node.Kind is BoundKind.Type
        || (node.Kind == BoundKind.Other
            && node.Operator is null
            && (node.Symbol is FunctionSymbol or SourceTypeSymbol or TestSymbol or ControllerSymbol or DatabaseSymbol
                || node.Children.Length == 0));

    private static void EmitLocal(BoundNode node, VariableSymbol variable, EmitContext context)
    {
        if (!ClrTypeMap.TryMapPrimitive(variable.Type, out _, out _))
        {
            context.Diagnostics.Add(new Diagnostic(
                DiagnosticCodes.EmitUnsupported,
                DiagnosticSeverity.Error,
                $"'{variable.Type.DisplayName}' tipi bu .NET backend yüzeyinde desteklenmiyor.",
                null,
                node.Span,
                1,
                1,
                null));
            return;
        }

        if (context.IsAsyncMoveNext)
        {
            context.AllocateSlot(variable);
            if (node.Children.Length == 0)
            {
                return;
            }

            EmitExpression(node.Children[0], context);
            EmitConvert(variable.Type, node.Children[0].Span, context);
            EmitStoreCheck(variable.Type.IsNullable, node.Span, context);
            EmitStoreLocal(variable, context);
            return;
        }

        var local = context.Il.DeclareLocal(typeof(UsValue));
        context.Locals[variable] = local;
        if (node.Children.Length == 0)
        {
            return;
        }

        EmitExpression(node.Children[0], context);
        EmitConvert(variable.Type, node.Children[0].Span, context);
        EmitStoreCheck(variable.Type.IsNullable, node.Span, context);
        context.Il.Emit(OpCodes.Stloc, local);
    }

    private static void EmitIf(BoundNode node, EmitContext context)
    {
        var end = context.Il.DefineLabel();
        var next = context.Il.DefineLabel();

        EmitCondition(node.Children[0], context);
        context.Il.Emit(OpCodes.Brfalse, next);
        EmitStatement(node.Children[1], context);
        context.Il.Emit(OpCodes.Br, end);
        context.Il.MarkLabel(next);

        var index = 2;
        while (index + 1 < node.Children.Length)
        {
            var elseIfFail = context.Il.DefineLabel();
            EmitCondition(node.Children[index], context);
            context.Il.Emit(OpCodes.Brfalse, elseIfFail);
            EmitStatement(node.Children[index + 1], context);
            context.Il.Emit(OpCodes.Br, end);
            context.Il.MarkLabel(elseIfFail);
            index += 2;
        }

        if (index < node.Children.Length)
        {
            EmitStatement(node.Children[index], context);
        }

        context.Il.MarkLabel(end);
    }

    private static void EmitLoop(BoundNode node, EmitContext context)
    {
        if (node.Operator is null && node.Children.Length == 1)
        {
            EmitStatement(node.Children[0], context);
            return;
        }

        switch (node.Operator)
        {
            case TokenKind.Tekrarla:
                EmitWhile(node, context);
                break;
            case TokenKind.Yap:
                EmitDoWhile(node, context);
                break;
            case TokenKind.Sonsuzdon:
                EmitForever(node, context);
                break;
            case TokenKind.Don when node.Symbol is not null && node.Children.Length >= 3:
                EmitRange(node, context);
                break;
            default:
                context.Diagnostics.Add(Unsupported(node, "Bu döngü biçimi henüz derlenmiyor."));
                break;
        }
    }

    private static void EmitWhile(BoundNode node, EmitContext context)
    {
        var continueLabel = context.Il.DefineLabel();
        var breakLabel = context.Il.DefineLabel();
        context.Loops.Push((breakLabel, continueLabel));
        context.Il.MarkLabel(continueLabel);
        EmitCondition(node.Children[0], context);
        context.Il.Emit(OpCodes.Brfalse, breakLabel);
        EmitStatement(node.Children[1], context);
        context.Il.Emit(OpCodes.Br, continueLabel);
        context.Il.MarkLabel(breakLabel);
        context.Loops.Pop();
    }

    private static void EmitDoWhile(BoundNode node, EmitContext context)
    {
        var body = context.Il.DefineLabel();
        var continueLabel = context.Il.DefineLabel();
        var breakLabel = context.Il.DefineLabel();
        context.Loops.Push((breakLabel, continueLabel));
        context.Il.MarkLabel(body);
        EmitStatement(node.Children[0], context);
        context.Il.MarkLabel(continueLabel);
        EmitCondition(node.Children[1], context);
        context.Il.Emit(OpCodes.Brtrue, body);
        context.Il.MarkLabel(breakLabel);
        context.Loops.Pop();
    }

    private static void EmitForever(BoundNode node, EmitContext context)
    {
        var continueLabel = context.Il.DefineLabel();
        var breakLabel = context.Il.DefineLabel();
        context.Loops.Push((breakLabel, continueLabel));
        context.Il.MarkLabel(continueLabel);
        EmitStatement(node.Children[0], context);
        context.Il.Emit(OpCodes.Br, continueLabel);
        context.Il.MarkLabel(breakLabel);
        context.Loops.Pop();
    }

    private static void EmitRange(BoundNode node, EmitContext context)
    {
        var hasStep = node.Children.Length == 4;
        var bodyIndex = node.Children.Length - 1;
        var startLocal = context.Il.DeclareLocal(typeof(int));
        var endLocal = context.Il.DeclareLocal(typeof(int));
        var stepLocal = context.Il.DeclareLocal(typeof(int));
        var directionLocal = context.Il.DeclareLocal(typeof(int));
        var currentLocal = context.Il.DeclareLocal(typeof(int));
        var loopVar = context.Il.DeclareLocal(typeof(UsValue));
        context.Locals[node.Symbol!] = loopVar;

        EmitExpression(node.Children[0], context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Children[0].Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Children[0].Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireInt))!);
        context.Il.Emit(OpCodes.Stloc, startLocal);

        EmitExpression(node.Children[1], context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Children[1].Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Children[1].Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireInt))!);
        context.Il.Emit(OpCodes.Stloc, endLocal);

        if (hasStep)
        {
            EmitExpression(node.Children[2], context);
            context.Il.Emit(OpCodes.Ldc_I4, node.Children[2].Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Children[2].Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireInt))!);
            context.Il.Emit(OpCodes.Stloc, stepLocal);
        }
        else
        {
            context.Il.Emit(OpCodes.Ldc_I4_1);
            context.Il.Emit(OpCodes.Stloc, stepLocal);
        }

        // direction = start == end ? 0 : start < end ? 1 : -1
        var dirZero = context.Il.DefineLabel();
        var dirPos = context.Il.DefineLabel();
        var dirDone = context.Il.DefineLabel();
        context.Il.Emit(OpCodes.Ldloc, startLocal);
        context.Il.Emit(OpCodes.Ldloc, endLocal);
        context.Il.Emit(OpCodes.Beq, dirZero);
        context.Il.Emit(OpCodes.Ldloc, startLocal);
        context.Il.Emit(OpCodes.Ldloc, endLocal);
        context.Il.Emit(OpCodes.Blt, dirPos);
        context.Il.Emit(OpCodes.Ldc_I4_M1);
        context.Il.Emit(OpCodes.Br, dirDone);
        context.Il.MarkLabel(dirPos);
        context.Il.Emit(OpCodes.Ldc_I4_1);
        context.Il.Emit(OpCodes.Br, dirDone);
        context.Il.MarkLabel(dirZero);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.MarkLabel(dirDone);
        context.Il.Emit(OpCodes.Stloc, directionLocal);

        // if step <= 0 throw
        var stepOk = context.Il.DefineLabel();
        context.Il.Emit(OpCodes.Ldloc, stepLocal);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Bgt, stepOk);
        context.Il.Emit(OpCodes.Ldstr, UsRuntimeCodes.ZeroStep);
        context.Il.Emit(OpCodes.Ldstr, "Adım sıfırdan büyük olmalıdır.");
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Newobj, typeof(UsRuntimeException).GetConstructor([typeof(string), typeof(string), typeof(int), typeof(int)])!);
        context.Il.Emit(OpCodes.Throw);
        context.Il.MarkLabel(stepOk);

        context.Il.Emit(OpCodes.Ldloc, startLocal);
        context.Il.Emit(OpCodes.Stloc, currentLocal);

        var continueLabel = context.Il.DefineLabel();
        var breakLabel = context.Il.DefineLabel();
        var head = context.Il.DefineLabel();
        context.Loops.Push((breakLabel, continueLabel));
        context.Il.MarkLabel(head);

        context.Il.Emit(OpCodes.Ldloc, currentLocal);
        context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromInt))!);
        context.Il.Emit(OpCodes.Stloc, loopVar);

        EmitStatement(node.Children[bodyIndex], context);

        context.Il.MarkLabel(continueLabel);
        // if direction == 0 break
        var afterDir = context.Il.DefineLabel();
        context.Il.Emit(OpCodes.Ldloc, directionLocal);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Beq, breakLabel);

        // next = current + direction * step
        context.Il.Emit(OpCodes.Ldloc, currentLocal);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Ldloc, directionLocal);
        context.Il.Emit(OpCodes.Ldloc, stepLocal);
        context.Il.Emit(OpCodes.Mul);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Add);
        var nextLocal = context.Il.DeclareLocal(typeof(long));
        context.Il.Emit(OpCodes.Stloc, nextLocal);

        // if direction > 0 && next > end || direction < 0 && next < end -> break
        var checkNeg = context.Il.DefineLabel();
        var keepGoing = context.Il.DefineLabel();
        context.Il.Emit(OpCodes.Ldloc, directionLocal);
        context.Il.Emit(OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ble, checkNeg);
        context.Il.Emit(OpCodes.Ldloc, nextLocal);
        context.Il.Emit(OpCodes.Ldloc, endLocal);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Bgt, breakLabel);
        context.Il.Emit(OpCodes.Br, keepGoing);
        context.Il.MarkLabel(checkNeg);
        context.Il.Emit(OpCodes.Ldloc, nextLocal);
        context.Il.Emit(OpCodes.Ldloc, endLocal);
        context.Il.Emit(OpCodes.Conv_I8);
        context.Il.Emit(OpCodes.Blt, breakLabel);
        context.Il.MarkLabel(keepGoing);

        context.Il.Emit(OpCodes.Ldloc, nextLocal);
        context.Il.Emit(OpCodes.Conv_I4);
        context.Il.Emit(OpCodes.Stloc, currentLocal);
        context.Il.Emit(OpCodes.Br, head);
        context.Il.MarkLabel(breakLabel);
        context.Loops.Pop();
        context.Locals.Remove(node.Symbol!);
    }

    private static void EmitJump(BoundNode node, EmitContext context)
    {
        if (context.Loops.Count == 0)
        {
            context.Diagnostics.Add(Unsupported(node, "Döngü dışında çık/devam."));
            return;
        }

        var (breakLabel, continueLabel) = context.Loops.Peek();
        if (node.Operator == TokenKind.Cik)
        {
            context.Il.Emit(OpCodes.Br, breakLabel);
            return;
        }

        if (node.Operator == TokenKind.Devam)
        {
            context.Il.Emit(OpCodes.Br, continueLabel);
            return;
        }

        context.Diagnostics.Add(Unsupported(node));
    }

    private static void EmitResult(BoundNode node, EmitContext context)
    {
        if (context.IsAsyncMoveNext)
        {
            // Evaluate result first so awaits do not leave a stale `this` under the stack.
            if (context.ReturnsValue)
            {
                if (node.Children.Length == 0)
                {
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                }
                else
                {
                    EmitExpression(node.Children[0], context);
                }

                var tmp = context.Il.DeclareLocal(typeof(UsValue));
                context.Il.Emit(OpCodes.Stloc, tmp);
                context.Il.Emit(OpCodes.Ldarg_0);
                context.Il.Emit(OpCodes.Ldloc, tmp);
                context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.CompleteValue))!);
            }
            else
            {
                if (node.Children.Length > 0)
                {
                    EmitExpression(node.Children[0], context);
                    context.Il.Emit(OpCodes.Pop);
                }

                context.Il.Emit(OpCodes.Ldarg_0);
                context.Il.Emit(OpCodes.Call, typeof(UsAsyncBox).GetMethod(nameof(UsAsyncBox.Complete))!);
            }

            EmitAsyncSuspendReturn(context);
            return;
        }

        if (context.ReturnsValue)
        {
            if (node.Children.Length == 0)
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }
            else
            {
                EmitExpression(node.Children[0], context);
            }

            context.Il.Emit(OpCodes.Ret);
            return;
        }

        if (node.Children.Length > 0)
        {
            EmitExpression(node.Children[0], context);
            context.Il.Emit(OpCodes.Pop);
        }

        context.Il.Emit(OpCodes.Ret);
    }

    private static void EmitCondition(BoundNode node, EmitContext context)
    {
        EmitExpression(node, context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireBool))!);
    }

    private static void EmitExpression(BoundNode node, EmitContext context)
    {
        switch (node.Kind)
        {
            case BoundKind.Literal:
                EmitLiteral(node, context);
                break;
            case BoundKind.Name:
                EmitNameLoad(node, context);
                break;
            case BoundKind.Unary:
                EmitUnary(node, context);
                break;
            case BoundKind.Binary:
                EmitBinary(node, context);
                break;
            case BoundKind.Assignment:
                EmitAssignment(node, context);
                break;
            case BoundKind.Call:
                EmitCall(node, context);
                break;
            case BoundKind.Member:
                EmitMember(node, context);
                break;
            case BoundKind.If when node.Operator == TokenKind.EqualsGreater:
                EmitExpressionIf(node, context);
                break;
            case BoundKind.Other when node.Operator == TokenKind.InterpolationStart:
                EmitInterpolation(node, context);
                break;
            case BoundKind.Other when node.Operator == TokenKind.OpenBracket && !node.Flag:
                EmitCollectionLiteral(node, context);
                break;
            case BoundKind.Other when node.Operator == TokenKind.OpenBracket && node.Flag:
                EmitIndex(node, context);
                break;
            case BoundKind.Other when node.Operator == TokenKind.Ile:
                EmitModelCopy(node, context);
                break;
            default:
                context.Diagnostics.Add(Unsupported(node));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                break;
        }
    }

    private static void EmitNameLoad(BoundNode node, EmitContext context)
    {
        if (node.Operator == TokenKind.Caret)
        {
            if (context.Caret is null)
            {
                context.Diagnostics.Add(Unsupported(node, "^ burada yok."));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
            }

            context.Il.Emit(OpCodes.Ldloc, context.Caret);
            return;
        }

        if (node.Operator is TokenKind.Bu or TokenKind.Temel)
        {
            EmitThis(context);
            return;
        }

        if (node.Symbol is FieldSymbol field)
        {
            EmitFieldLoad(field, node.Span, context);
            return;
        }

        // `bağla db` is a process-wide handle; controller/async methods may not share Main locals.
        if (node.Symbol is VariableSymbol { Type: DatabaseTypeSymbol })
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitDatabase).GetMethod(nameof(UsEmitDatabase.Bind))!);
            return;
        }

        if (TryEmitClockName(node, context))
        {
            return;
        }

        if (node.Symbol is null)
        {
            context.Diagnostics.Add(Unsupported(node, "Yerel değişken bulunamadı."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (context.ByRefArgIndexes.TryGetValue(node.Symbol, out var byRefIndex))
        {
            EmitLoadByRefParameter(byRefIndex, context);
            return;
        }

        if (context.IsAsyncMoveNext)
        {
            EmitLoadLocal(node.Symbol, context);
            return;
        }

        if (!context.Locals.TryGetValue(node.Symbol, out var local))
        {
            context.Diagnostics.Add(Unsupported(node, $"'{node.Symbol.Name}' yerel değişkeni bulunamadı."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        context.Il.Emit(OpCodes.Ldloc, local);
    }

    private static void EmitLiteral(BoundNode node, EmitContext context)
    {
        if (node.Constant == "~")
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        var text = node.Constant ?? string.Empty;
        if (!ClrTypeMap.TryMapPrimitive(node.Type, out _, out var kind))
        {
            context.Diagnostics.Add(Unsupported(node, "Literal tipi desteklenmiyor."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        switch (kind)
        {
            case UsValueKind.Text:
                context.Il.Emit(OpCodes.Ldstr, text);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralText))!);
                break;
            case UsValueKind.Int:
                context.Il.Emit(OpCodes.Ldc_I4, int.Parse(text, CultureInfo.InvariantCulture));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralInt))!);
                break;
            case UsValueKind.UInt:
                context.Il.Emit(OpCodes.Ldc_I4, unchecked((int)uint.Parse(text, CultureInfo.InvariantCulture)));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralUInt))!);
                break;
            case UsValueKind.Float64:
                context.Il.Emit(OpCodes.Ldc_R8, double.Parse(text, CultureInfo.InvariantCulture));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralFloat64))!);
                break;
            case UsValueKind.Float32:
                context.Il.Emit(OpCodes.Ldc_R4, float.Parse(text, CultureInfo.InvariantCulture));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralFloat32))!);
                break;
            case UsValueKind.Decimal:
                EmitDecimalLiteral(text, context);
                break;
            case UsValueKind.Bool:
                context.Il.Emit(text == "doğru" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
                break;
            case UsValueKind.Char:
                context.Il.Emit(OpCodes.Ldc_I4, text.Length == 0 ? 0 : text[0]);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralChar))!);
                break;
            default:
                context.Diagnostics.Add(Unsupported(node, "Literal tipi desteklenmiyor."));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                break;
        }
    }

    private static void EmitDecimalLiteral(string text, EmitContext context)
    {
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        var bits = decimal.GetBits(value);
        context.Il.Emit(OpCodes.Ldc_I4, bits[0]);
        context.Il.Emit(OpCodes.Ldc_I4, bits[1]);
        context.Il.Emit(OpCodes.Ldc_I4, bits[2]);
        context.Il.Emit(bits[3] < 0 ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ldc_I4, (bits[3] >> 16) & 0xFF);
        context.Il.Emit(OpCodes.Newobj, typeof(decimal).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(bool), typeof(byte)])!);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralDecimal))!);
    }

    private static void EmitUnary(BoundNode node, EmitContext context)
    {
        var operand = node.Children[0];
        if (node.Operator is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            EmitNameLoad(operand, context);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireNumber))!);
            var current = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, current);
            context.Il.Emit(OpCodes.Ldloc, current);
            context.Il.Emit(OpCodes.Ldc_I4_1);
            context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromInt))!);
            EmitKindOf(operand.Type, context);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            var method = node.Operator == TokenKind.PlusPlus
                ? typeof(UsMath).GetMethod(nameof(UsMath.Add))!
                : typeof(UsMath).GetMethod(nameof(UsMath.Subtract))!;
            context.Il.Emit(OpCodes.Call, method);
            var updated = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, updated);
            EmitStoreTarget(operand, () => context.Il.Emit(OpCodes.Ldloc, updated), context);
            if (node.Flag)
            {
                context.Il.Emit(OpCodes.Ldloc, current);
            }
            else
            {
                context.Il.Emit(OpCodes.Ldloc, updated);
            }

            return;
        }

        if (node.Operator == TokenKind.Bekle)
        {
            EmitAwaitUnary(node, context);
            return;
        }

        EmitExpression(operand, context);
        switch (node.Operator)
        {
            case TokenKind.Bang:
                context.Il.Emit(OpCodes.Ldc_I4, operand.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, operand.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireBool))!);
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Ceq);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
                break;
            case TokenKind.Minus:
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsMath).GetMethod(nameof(UsMath.Negate))!);
                break;
            case TokenKind.Plus:
                break;
            default:
                context.Diagnostics.Add(Unsupported(node));
                break;
        }
    }

    private static void EmitBinary(BoundNode node, EmitContext context)
    {
        var leftNode = node.Children[0];
        var rightNode = node.Children[1];
        if (node.Operator == TokenKind.AmpersandAmpersand)
        {
            var falseLabel = context.Il.DefineLabel();
            var end = context.Il.DefineLabel();
            EmitCondition(leftNode, context);
            context.Il.Emit(OpCodes.Brfalse, falseLabel);
            EmitCondition(rightNode, context);
            context.Il.Emit(OpCodes.Br, end);
            context.Il.MarkLabel(falseLabel);
            context.Il.Emit(OpCodes.Ldc_I4_0);
            context.Il.MarkLabel(end);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
            return;
        }

        if (node.Operator == TokenKind.PipePipe)
        {
            var trueLabel = context.Il.DefineLabel();
            var end = context.Il.DefineLabel();
            EmitCondition(leftNode, context);
            context.Il.Emit(OpCodes.Brtrue, trueLabel);
            EmitCondition(rightNode, context);
            context.Il.Emit(OpCodes.Br, end);
            context.Il.MarkLabel(trueLabel);
            context.Il.Emit(OpCodes.Ldc_I4_1);
            context.Il.MarkLabel(end);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
            return;
        }

        if (node.Operator == TokenKind.QuestionQuestion)
        {
            EmitExpression(leftNode, context);
            var left = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, left);
            context.Il.Emit(OpCodes.Ldloc, left);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.IsNull))!);
            var useRight = context.Il.DefineLabel();
            var end = context.Il.DefineLabel();
            context.Il.Emit(OpCodes.Brtrue, useRight);
            context.Il.Emit(OpCodes.Ldloc, left);
            EmitConvert(node.Type, leftNode.Span, context);
            context.Il.Emit(OpCodes.Br, end);
            context.Il.MarkLabel(useRight);
            EmitExpression(rightNode, context);
            context.Il.MarkLabel(end);
            return;
        }

        if (TryEmitTemporalBinary(node, context))
        {
            return;
        }

        EmitExpression(leftNode, context);
        EmitExpression(rightNode, context);
        var op = node.Operator;
        if (op is TokenKind.EqualEqual or TokenKind.BangEqual)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.EqualsModel))!);
            if (op == TokenKind.BangEqual)
            {
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Ceq);
            }

            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
            return;
        }

        if (op is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            context.Il.Emit(OpCodes.Ldstr, OperatorText(op.Value));
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsMath).GetMethod(nameof(UsMath.Compare))!);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralBool))!);
            return;
        }

        EmitKindOf(node.Type, context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        var arithmetic = op switch
        {
            TokenKind.Plus => typeof(UsMath).GetMethod(nameof(UsMath.Add))!,
            TokenKind.Minus => typeof(UsMath).GetMethod(nameof(UsMath.Subtract))!,
            TokenKind.Star => typeof(UsMath).GetMethod(nameof(UsMath.Multiply))!,
            TokenKind.Slash => typeof(UsMath).GetMethod(nameof(UsMath.Divide))!,
            TokenKind.Percent => typeof(UsMath).GetMethod(nameof(UsMath.Remainder))!,
            _ => null,
        };
        if (arithmetic is null)
        {
            context.Diagnostics.Add(Unsupported(node));
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        context.Il.Emit(OpCodes.Call, arithmetic);
    }

    private static void EmitAssignment(BoundNode node, EmitContext context)
    {
        var left = node.Children[0];
        var right = node.Children[1];
        if (node.Operator is TokenKind.PlusEquals or TokenKind.MinusEquals && left.Symbol is EventSymbol)
        {
            EmitEventAssignment(node, context);
            return;
        }

        if (node.Operator == TokenKind.Equals)
        {
            EmitExpression(right, context);
            EmitConvert(left.Type, right.Span, context);
            EmitStoreCheck(left.Type.IsNullable, node.Span, context);
            var value = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, value);
            EmitStoreTarget(left, () => context.Il.Emit(OpCodes.Ldloc, value), context);
            context.Il.Emit(OpCodes.Ldloc, value);
            return;
        }

        EmitNameLoad(left, context);
        EmitExpression(right, context);
        EmitKindOf(left.Type, context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        var method = node.Operator switch
        {
            TokenKind.PlusEquals => typeof(UsMath).GetMethod(nameof(UsMath.Add))!,
            TokenKind.MinusEquals => typeof(UsMath).GetMethod(nameof(UsMath.Subtract))!,
            TokenKind.StarEquals => typeof(UsMath).GetMethod(nameof(UsMath.Multiply))!,
            TokenKind.SlashEquals => typeof(UsMath).GetMethod(nameof(UsMath.Divide))!,
            _ => null,
        };
        if (method is null)
        {
            context.Diagnostics.Add(Unsupported(node));
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Pop);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        context.Il.Emit(OpCodes.Call, method);
        var updated = context.Il.DeclareLocal(typeof(UsValue));
        context.Il.Emit(OpCodes.Stloc, updated);
        EmitStoreTarget(left, () => context.Il.Emit(OpCodes.Ldloc, updated), context);
        context.Il.Emit(OpCodes.Ldloc, updated);
    }

    private static void EmitStoreTarget(BoundNode target, Action loadValue, EmitContext context)
    {
        if (target.Kind == BoundKind.Member && target.Symbol is FieldSymbol field)
        {
            EmitExpression(target.Children[0], context);
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            loadValue();
            context.Il.Emit(OpCodes.Ldc_I4, target.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, target.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
            return;
        }

        if (target.Symbol is FieldSymbol implicitField)
        {
            EmitFieldStore(implicitField, loadValue, target.Span, context);
            return;
        }

        if (target.Kind == BoundKind.Other && target.Operator == TokenKind.OpenBracket && target.Flag)
        {
            EmitExpression(target.Children[0], context);
            EmitExpression(target.Children[1], context);
            loadValue();
            context.Il.Emit(OpCodes.Ldc_I4, target.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, target.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Set))!);
            return;
        }

        if (target.Symbol is null)
        {
            context.Diagnostics.Add(Unsupported(target, "Atama hedefi bulunamadı."));
            loadValue();
            context.Il.Emit(OpCodes.Pop);
            return;
        }

        if (context.ByRefArgIndexes.TryGetValue(target.Symbol, out var byRefIndex))
        {
            EmitStoreByRefParameter(byRefIndex, loadValue, context);
            return;
        }

        if (context.IsAsyncMoveNext)
        {
            loadValue();
            EmitStoreLocal(target.Symbol, context);
            return;
        }

        if (!context.Locals.TryGetValue(target.Symbol, out var local))
        {
            context.Diagnostics.Add(Unsupported(target, "Atama hedefi bulunamadı."));
            loadValue();
            context.Il.Emit(OpCodes.Pop);
            return;
        }

        loadValue();
        context.Il.Emit(OpCodes.Stloc, local);
    }

    private static void EmitConstruct(BoundNode node, EmitContext context)
    {
        var sourceType = node.Type as SourceTypeSymbol
            ?? (node.Type as ConstructedTypeSymbol)?.Definition as SourceTypeSymbol;
        if (sourceType is null)
        {
            context.Diagnostics.Add(Unsupported(node, "Oluşturma tipi yok."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        var fields = CollectFields(sourceType);
        context.Il.Emit(OpCodes.Ldstr, sourceType.Name);
        context.Il.Emit(OpCodes.Ldc_I4, fields.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(string));
        for (var index = 0; index < fields.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(OpCodes.Ldstr, fields[index].Name);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        context.Il.Emit(OpCodes.Ldc_I4, fields.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(bool));
        for (var index = 0; index < fields.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(fields[index].Type.IsNullable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            context.Il.Emit(OpCodes.Stelem_I1);
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.Create))!);
        var instance = context.Il.DeclareLocal(typeof(UsValue));
        context.Il.Emit(OpCodes.Stloc, instance);

        foreach (var field in fields)
        {
            if (field.Default is not null)
            {
                context.Il.Emit(OpCodes.Ldloc, instance);
                context.Il.Emit(OpCodes.Ldstr, field.Name);
                EmitExpression(field.Default, context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
                continue;
            }

            // Class fields get zero/null defaults (interpreter ApplyDefaults); models require explicit init.
            if (sourceType.TypeKind == SourceTypeKind.Model)
            {
                continue;
            }

            context.Il.Emit(OpCodes.Ldloc, instance);
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            if (!TryEmitZero(field.Type, context))
            {
                context.Il.Emit(OpCodes.Pop);
                context.Il.Emit(OpCodes.Pop);
                continue;
            }

            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
        }

        if (node.Symbol is FunctionSymbol constructor && constructor.BoundBody is not null)
        {
            if (HasByRefParameters(constructor))
            {
                context.Diagnostics.Add(Unsupported(node, $"'{constructor.Name}' oluşturucusunda ref/çıktı henüz derlenmiyor."));
                return;
            }

            var args = node.Children.Where(item => !item.Flag).ToArray();
            var saved = new Dictionary<Symbol, LocalBuilder>();
            foreach (var pair in context.Locals)
            {
                saved[pair.Key] = pair.Value;
            }

            // Receiver for field assignment targets named via FieldSymbol on left of assignment:
            // Store instance under a synthetic approach: field writes in ctor use Name(FieldSymbol).
            for (var index = 0; index < constructor.Parameters.Count; index++)
            {
                var parameter = constructor.Parameters[index];
                if (index < args.Length)
                {
                    EmitExpression(args[index], context);
                }
                else if (parameter.Default is not null)
                {
                    EmitExpression(parameter.Default, context);
                }
                else
                {
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                }

                // Prefer EmitStoreLocal so async MoveNext (AsyncSlots) and sync Locals stay consistent.
                if (context.IsAsyncMoveNext)
                {
                    EmitStoreLocal(parameter, context);
                }
                else
                {
                    var local = context.Il.DeclareLocal(typeof(UsValue));
                    context.Il.Emit(OpCodes.Stloc, local);
                    context.Locals[parameter] = local;
                }
            }

            var previousThis = context.ThisLocal;
            context.ThisLocal = instance;
            EmitConstructorBody(constructor.BoundBody, instance, context);
            context.ThisLocal = previousThis;
            foreach (var parameter in constructor.Parameters)
            {
                context.Locals.Remove(parameter);
            }

            foreach (var pair in saved)
            {
                context.Locals[pair.Key] = pair.Value;
            }
        }

        foreach (var child in node.Children.Where(item => item.Flag))
        {
            if (child.Symbol is not FieldSymbol field)
            {
                continue;
            }

            context.Il.Emit(OpCodes.Ldloc, instance);
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            if (child.Children.Length > 0)
            {
                EmitExpression(child.Children[0], context);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }

            context.Il.Emit(OpCodes.Ldc_I4, child.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, child.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
        }

        context.Il.Emit(OpCodes.Ldloc, instance);
    }

    private static void EmitConstructorBody(BoundNode body, LocalBuilder instance, EmitContext context)
    {
        switch (body.Kind)
        {
            case BoundKind.Block:
                foreach (var child in body.Children)
                {
                    EmitConstructorBody(child, instance, context);
                }

                break;
            case BoundKind.Assignment when body.Children[0].Symbol is FieldSymbol field:
                context.Il.Emit(OpCodes.Ldloc, instance);
                context.Il.Emit(OpCodes.Ldstr, field.Name);
                EmitExpression(body.Children[1], context);
                context.Il.Emit(OpCodes.Ldc_I4, body.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, body.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.WriteField))!);
                break;
            default:
                EmitStatement(body, context);
                break;
        }
    }

    private static void EmitMember(BoundNode node, EmitContext context)
    {
        if (node.Constant is "milisaniye" or "saniye" or "dakika" or "saat" or "gün" or "hafta")
        {
            EmitExpression(node.Children[0], context);
            context.Il.Emit(OpCodes.Ldstr, node.Constant);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Duration))!);
            return;
        }

        if (node.Constant is "adet" or "uzunluk")
        {
            EmitExpression(node.Children[0], context);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Count))!);
            context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromInt))!);
            return;
        }

        if (node.Constant is "ilk")
        {
            EmitExpression(node.Children[0], context);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.First))!);
            return;
        }

        if (TryEmitTableMember(node, context))
        {
            return;
        }

        if (node.Symbol is FieldSymbol field)
        {
            EmitExpression(node.Children[0], context);
            context.Il.Emit(OpCodes.Ldstr, field.Name);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.ReadField))!);
            return;
        }

        context.Diagnostics.Add(Unsupported(node, "Üye erişimi desteklenmiyor."));
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
    }

    private static void EmitCollectionLiteral(BoundNode node, EmitContext context)
    {
        var kind = node.Type.WithNullable(false).Name;
        if (kind is not ("liste" or "dizi" or "küme" or "sliste"))
        {
            kind = "liste";
        }

        context.Il.Emit(OpCodes.Ldstr, kind);
        context.Il.Emit(OpCodes.Ldc_I4, node.Children.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(UsValue));
        for (var index = 0; index < node.Children.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            EmitExpression(node.Children[index], context);
            context.Il.Emit(OpCodes.Stelem, typeof(UsValue));
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Create))!);
    }

    private static void EmitIndex(BoundNode node, EmitContext context)
    {
        EmitExpression(node.Children[0], context);
        EmitExpression(node.Children[1], context);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Get))!);
    }

    private static void EmitModelCopy(BoundNode node, EmitContext context)
    {
        EmitExpression(node.Children[0], context);
        var initializers = node.Children.Skip(1).Where(item => item.Symbol is FieldSymbol).ToArray();
        context.Il.Emit(OpCodes.Ldc_I4, initializers.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(string));
        for (var index = 0; index < initializers.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(OpCodes.Ldstr, ((FieldSymbol)initializers[index].Symbol!).Name);
            context.Il.Emit(OpCodes.Stelem_Ref);
        }

        context.Il.Emit(OpCodes.Ldc_I4, initializers.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(UsValue));
        for (var index = 0; index < initializers.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            if (initializers[index].Children.Length > 0)
            {
                EmitExpression(initializers[index].Children[0], context);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }

            context.Il.Emit(OpCodes.Stelem, typeof(UsValue));
        }

        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitObject).GetMethod(nameof(UsEmitObject.Copy))!);
    }

    private static void EmitQueryCall(BoundNode node, EmitContext context)
    {
        var name = node.Constant!;
        if (node.Children.Length == 0)
        {
            context.Diagnostics.Add(Unsupported(node));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (TryEmitTableQueryCall(node, context))
        {
            return;
        }

        EmitExpression(node.Children[0], context);
        switch (name)
        {
            case "filtrele" when node.Children.Length >= 2:
                EmitPredicateDelegate(node.Children[1], context, typeof(Func<UsValue, bool>));
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Filter))!);
                return;
            case "seç" when node.Children.Length >= 2:
                EmitPredicateDelegate(node.Children[1], context, typeof(Func<UsValue, UsValue>));
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Select))!);
                return;
            case "ekle" when node.Children.Length >= 2:
                EmitExpression(node.Children[1], context);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.Add))!);
                return;
            case "ilk":
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
                context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitList).GetMethod(nameof(UsEmitList.First))!);
                return;
            default:
                context.Diagnostics.Add(Unsupported(node, $"Sorgu '{name}' henüz derlenmiyor."));
                context.Il.Emit(OpCodes.Pop);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
        }
    }

    private static void EmitPredicateDelegate(BoundNode body, EmitContext context, Type delegateType)
    {
        var returnsBool = delegateType == typeof(Func<UsValue, bool>);
        var method = context.ProgramType.DefineMethod(
            "UsLambda_" + context.LambdaSerial++,
            MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
            returnsBool ? typeof(bool) : typeof(UsValue),
            [typeof(UsValue)]);
        var bodyIl = method.GetILGenerator();
        var nested = new EmitContext(bodyIl, context.Methods, context.ProgramType, returnsValue: !returnsBool)
        {
            Caret = bodyIl.DeclareLocal(typeof(UsValue)),
        };
        bodyIl.Emit(OpCodes.Ldarg_0);
        bodyIl.Emit(OpCodes.Stloc, nested.Caret);
        EmitExpression(body, nested);
        context.Diagnostics.AddRange(nested.Diagnostics);
        if (returnsBool)
        {
            bodyIl.Emit(OpCodes.Ldc_I4, body.Span.Start);
            bodyIl.Emit(OpCodes.Ldc_I4, body.Span.Length);
            bodyIl.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.RequireBool))!);
        }

        bodyIl.Emit(OpCodes.Ret);

        context.Il.Emit(OpCodes.Ldnull);
        context.Il.Emit(OpCodes.Ldftn, method);
        context.Il.Emit(OpCodes.Newobj, delegateType.GetConstructor([typeof(object), typeof(IntPtr)])!);
    }

    private static void EmitInstanceMethodCall(BoundNode node, FunctionSymbol function, EmitContext context)
    {
        // Instance methods: first child is receiver; remaining children are arguments.
        var target = function.Template ?? function;
        if (!context.Methods.TryGetValue(target, out var method) && !context.Methods.TryGetValue(function, out method))
        {
            context.Diagnostics.Add(Unsupported(node, $"Örnek metod '{function.Name}' henüz derlenmiyor."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (node.Children.Length == 0)
        {
            context.Diagnostics.Add(Unsupported(node, "Örnek metod alıcısı yok."));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (target.IsAsync && HasByRefParameters(target))
        {
            context.Diagnostics.Add(AsyncByRefUnsupported(target, node.Span));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        // Virtual dispatch for call sites: prefer override registered under the virtual slot.
        if (IsVirtualSlot(function)
            && !target.IsAsync
            && target.Parameters.Count <= 1
            && target.ReturnType is UnitTypeSymbol
            && !HasByRefParameters(target))
        {
            EmitExpression(node.Children[0], context);
            var selfLocal = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, selfLocal);
            context.Il.Emit(OpCodes.Ldloc, selfLocal);
            context.Il.Emit(OpCodes.Ldstr, VirtualSlotKey(function));
            if (target.Parameters.Count == 1)
            {
                if (node.Children.Length > 1)
                {
                    EmitExpression(node.Children[1], context);
                }
                else if (target.Parameters[0].Default is { } defaultNode)
                {
                    EmitExpression(defaultNode, context);
                }
                else
                {
                    context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                }
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }

            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Start);
            context.Il.Emit(OpCodes.Ldc_I4, node.Span.Length);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitVTable).GetMethod(nameof(UsEmitVTable.InvokeVirtual))!);
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        EmitExpression(node.Children[0], context);
        for (var index = 0; index < target.Parameters.Count; index++)
        {
            var parameter = target.Parameters[index];
            var childIndex = index + 1;
            if (IsByRefParameter(parameter))
            {
                if (childIndex < node.Children.Length)
                {
                    EmitArgumentAddress(node.Children[childIndex], context);
                }
                else
                {
                    context.Diagnostics.Add(Unsupported(node, $"'{parameter.Name}' ref/çıktı argümanı eksik."));
                    var dummy = context.Il.DeclareLocal(typeof(UsValue));
                    context.Il.Emit(OpCodes.Ldloca, dummy);
                }

                continue;
            }

            if (childIndex < node.Children.Length)
            {
                EmitExpression(node.Children[childIndex], context);
            }
            else if (parameter.Default is { } defaultNode)
            {
                EmitExpression(defaultNode, context);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }
        }

        context.Il.Emit(OpCodes.Call, method);
        if (target.IsAsync)
        {
            if (target.ReturnType is UnitTypeSymbol)
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Box), [typeof(Task)])!);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Box), [typeof(Task<UsValue>)])!);
            }

            return;
        }

        if (target.ReturnType is UnitTypeSymbol)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }
    }

    private static void EmitCall(BoundNode node, EmitContext context)
    {
        if (node.Operator == TokenKind.At)
        {
            EmitConstruct(node, context);
            return;
        }

        if (node.Constant == "tetikle")
        {
            EmitEventTrigger(node, context);
            return;
        }

        if (node.Symbol is null && node.Constant is not null)
        {
            EmitQueryCall(node, context);
            return;
        }

        if (node.Symbol is not FunctionSymbol function)
        {
            context.Diagnostics.Add(Unsupported(node));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (function.IsConstructor)
        {
            EmitConstruct(node, context);
            return;
        }

        if (function.Intrinsic != Intrinsic.None && TryEmitStdlibIntrinsic(node, function, context))
        {
            return;
        }

        if (function.Intrinsic != Intrinsic.None && TryEmitHttpIntrinsic(node, function, context))
        {
            return;
        }

        if (function.DeclaringType is not null)
        {
            EmitInstanceMethodCall(node, function, context);
            return;
        }

        switch (function.Intrinsic)
        {
            case Intrinsic.WriteLine:
                EmitExpression(node.Children[0], context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.WriteLine))!);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
            case Intrinsic.Write:
                EmitExpression(node.Children[0], context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Write))!);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
            case Intrinsic.Clear:
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Clear))!);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
            case Intrinsic.ReadLine:
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.ReadLine))!);
                return;
            case Intrinsic.Ask:
                EmitExpression(node.Children[0], context);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Ask))!);
                return;
            case Intrinsic.None:
                EmitUserCall(node, function, context);
                return;
            default:
                context.Diagnostics.Add(Unsupported(node, "Bu içsel henüz derlenmiyor."));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
        }
    }

    private static void EmitUserCall(BoundNode node, FunctionSymbol function, EmitContext context)
    {
        var target = function.Template ?? function;
        if (target.IsAsync && HasByRefParameters(target))
        {
            context.Diagnostics.Add(AsyncByRefUnsupported(target, node.Span));
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return;
        }

        if (!context.Methods.TryGetValue(target, out var method) && !context.Methods.TryGetValue(function, out method))
        {
            // Overload resolution already chose function; try match by name+arity among known methods.
            var match = context.Methods.Keys.FirstOrDefault(item =>
                item.Name == target.Name && item.Parameters.Count == target.Parameters.Count);
            if (match is null || !context.Methods.TryGetValue(match, out method))
            {
                var detail = HasByRefParameters(target)
                    ? $"Fonksiyon '{function.Name}' ref/çıktı ile emit edilmedi."
                    : $"Fonksiyon '{function.Name}' emit edilmedi.";
                context.Diagnostics.Add(Unsupported(node, detail));
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
                return;
            }
        }

        for (var index = 0; index < target.Parameters.Count; index++)
        {
            var parameter = target.Parameters[index];
            if (IsByRefParameter(parameter))
            {
                if (index < node.Children.Length)
                {
                    EmitArgumentAddress(node.Children[index], context);
                }
                else
                {
                    context.Diagnostics.Add(Unsupported(node, $"'{parameter.Name}' ref/çıktı argümanı eksik."));
                    var dummy = context.Il.DeclareLocal(typeof(UsValue));
                    context.Il.Emit(OpCodes.Ldloca, dummy);
                }

                continue;
            }

            if (index < node.Children.Length)
            {
                EmitExpression(node.Children[index], context);
            }
            else if (parameter.Default is { } defaultNode)
            {
                EmitExpression(defaultNode, context);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            }
        }

        context.Il.Emit(OpCodes.Call, method);
        if (target.IsAsync)
        {
            if (target.ReturnType is UnitTypeSymbol)
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Box), [typeof(Task)])!);
            }
            else
            {
                context.Il.Emit(OpCodes.Call, typeof(UsEmitAsync).GetMethod(nameof(UsEmitAsync.Box), [typeof(Task<UsValue>)])!);
            }

            return;
        }

        if (target.ReturnType is UnitTypeSymbol)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
        }
    }

    private static void EmitExpressionIf(BoundNode node, EmitContext context)
    {
        var elseLabel = context.Il.DefineLabel();
        var end = context.Il.DefineLabel();
        EmitCondition(node.Children[0], context);
        context.Il.Emit(OpCodes.Brfalse, elseLabel);
        EmitExpression(node.Children[1], context);
        context.Il.Emit(OpCodes.Br, end);
        context.Il.MarkLabel(elseLabel);
        EmitExpression(node.Children[2], context);
        context.Il.MarkLabel(end);
    }

    private static void EmitInterpolation(BoundNode node, EmitContext context)
    {
        // Pre-evaluate parts so awaits do not leave array/index under the evaluation stack.
        var parts = new LocalBuilder[node.Children.Length];
        for (var index = 0; index < node.Children.Length; index++)
        {
            EmitExpression(node.Children[index], context);
            parts[index] = context.Il.DeclareLocal(typeof(UsValue));
            context.Il.Emit(OpCodes.Stloc, parts[index]);
        }

        context.Il.Emit(OpCodes.Ldc_I4, node.Children.Length);
        context.Il.Emit(OpCodes.Newarr, typeof(UsValue));
        for (var index = 0; index < node.Children.Length; index++)
        {
            context.Il.Emit(OpCodes.Dup);
            context.Il.Emit(OpCodes.Ldc_I4, index);
            context.Il.Emit(OpCodes.Ldloc, parts[index]);
            context.Il.Emit(OpCodes.Stelem, typeof(UsValue));
        }

        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Interpolation))!);
    }

    private static void EmitConvert(TypeSymbol type, TextSpan span, EmitContext context)
    {
        if (!ClrTypeMap.TryMapPrimitive(type, out _, out var kind))
        {
            return;
        }

        if (kind is UsValueKind.Object or UsValueKind.Text or UsValueKind.Bool or UsValueKind.Char)
        {
            // UsEmitOps.Convert still handles null / same-kind
        }

        context.Il.Emit(OpCodes.Ldc_I4, (int)kind);
        context.Il.Emit(OpCodes.Ldc_I4, span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Convert))!);
    }

    private static void EmitStoreCheck(bool nullable, TextSpan span, EmitContext context)
    {
        context.Il.Emit(nullable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        context.Il.Emit(OpCodes.Ldc_I4, span.Start);
        context.Il.Emit(OpCodes.Ldc_I4, span.Length);
        context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Store))!);
    }

    private static void EmitKindOf(TypeSymbol type, EmitContext context)
    {
        if (!ClrTypeMap.TryMapPrimitive(type, out _, out var kind))
        {
            context.Il.Emit(OpCodes.Ldc_I4, (int)UsValueKind.Int);
            return;
        }

        context.Il.Emit(OpCodes.Ldc_I4, (int)kind);
    }

    private static string OperatorText(TokenKind kind) => kind switch
    {
        TokenKind.EqualEqual => "==",
        TokenKind.BangEqual => "!=",
        TokenKind.Less => "<",
        TokenKind.LessEqual => "<=",
        TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=",
        _ => "==",
    };

    /// <summary>
    /// Emits a zero/empty default UsValue for class field initialization. Returns false when the type has no zero.
    /// </summary>
    private static bool TryEmitZero(TypeSymbol type, EmitContext context)
    {
        if (type.IsNullable)
        {
            context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Null))!);
            return true;
        }

        switch (type.WithNullable(false).Name)
        {
            case "mantıksal":
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromBool))!);
                return true;
            case "karakter":
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromChar))!);
                return true;
            case "tamsayı":
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromInt))!);
                return true;
            case "utamsayı":
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromUInt))!);
                return true;
            case "ondalık":
                context.Il.Emit(OpCodes.Ldc_R8, 0.0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromFloat64))!);
                return true;
            case "gondalık":
                context.Il.Emit(OpCodes.Ldc_R4, 0.0f);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromFloat32))!);
                return true;
            case "hondalık":
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsValue).GetMethod(nameof(UsValue.FromInt))!);
                context.Il.Emit(OpCodes.Ldc_I4, (int)UsValueKind.Decimal);
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Ldc_I4_0);
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.Convert))!);
                return true;
            case "metin":
                context.Il.Emit(OpCodes.Ldstr, "");
                context.Il.Emit(OpCodes.Call, typeof(UsEmitOps).GetMethod(nameof(UsEmitOps.FromLiteralText))!);
                return true;
            default:
                return false;
        }
    }

    private static Diagnostic Unsupported(BoundNode node, string? detail = null) =>
        new(
            DiagnosticCodes.EmitUnsupported,
            DiagnosticSeverity.Error,
            detail ?? $"'{node.Kind}' düğümü bu .NET backend yüzeyinde desteklenmiyor.",
            null,
            node.Span,
            1,
            1,
            "Parity matrisi: docs/compiler-parity.md");
}
