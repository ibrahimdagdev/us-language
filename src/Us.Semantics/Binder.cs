using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;
using Us.Parser;

namespace Us.Semantics;

public sealed class Binder
{
    private SourceFile _file;
    private readonly DiagnosticBag _diagnostics = new();
    private readonly Scope _root;
    private readonly PrimitiveTypeSymbol _metin;
    private readonly PrimitiveTypeSymbol _karakter;
    private readonly PrimitiveTypeSymbol _tamsayı;
    private readonly PrimitiveTypeSymbol _utamsayı;
    private readonly PrimitiveTypeSymbol _ondalık;
    private readonly PrimitiveTypeSymbol _gondalık;
    private readonly PrimitiveTypeSymbol _hondalık;
    private readonly PrimitiveTypeSymbol _mantıksal;
    private readonly PrimitiveTypeSymbol _nesne;
    private readonly PrimitiveTypeSymbol _dinamik;
    private readonly PrimitiveTypeSymbol _tarih;
    private readonly PrimitiveTypeSymbol _saat;
    private readonly PrimitiveTypeSymbol _tarihsaat;
    private readonly PrimitiveTypeSymbol _süre;
    private readonly SourceTypeSymbol _hata;
    private readonly List<SourceTypeSymbol> _types = [];
    private readonly List<(FunctionSymbol Symbol, FunctionDeclarationSyntax Syntax, Scope Scope, SourceFile File)> _functions = [];
    private readonly List<(FunctionSymbol Symbol, ConstructorDeclarationSyntax Syntax, Scope Scope, SourceFile File)> _constructors = [];
    private readonly List<MappingSymbol> _mappings = [];
    private readonly List<TestSymbol> _tests = [];
    private readonly List<ServiceRegistration> _services = [];
    private readonly List<DatabaseSymbol> _databases = [];
    private readonly Dictionary<DatabaseDeclarationSyntax, DatabaseSymbol> _databaseSyntax = [];
    private readonly List<ControllerSymbol> _controllers = [];
    private readonly List<RouteSymbol> _routes = [];
    private readonly List<(ControllerDeclarationSyntax Syntax, ControllerSymbol Symbol, Scope Scope, SourceFile File)> _controllerBodies = [];
    private TypeSymbol? _httpBuilderType;
    private TypeSymbol? _httpResponseType;
    private TypeSymbol? _httpResultType;
    private Scope _scope;
    private Flow _flow = Flow.Empty;
    private TypeSymbol? _currentItem;
    private SourceTypeSymbol? _currentType;
    private FunctionSymbol? _function;
    private int _loopDepth;
    private int _transactionDepth;
    private bool _transactionClosed;
    private bool _awaitAllowed = true;
    private bool _filePublic;
    private bool _staticContext;

    private Binder(SourceFile file)
    {
        _file = file;
        _metin = new PrimitiveTypeSymbol("metin");
        _karakter = new PrimitiveTypeSymbol("karakter");
        _tamsayı = new PrimitiveTypeSymbol("tamsayı");
        _utamsayı = new PrimitiveTypeSymbol("utamsayı");
        _ondalık = new PrimitiveTypeSymbol("ondalık");
        _gondalık = new PrimitiveTypeSymbol("gondalık");
        _hondalık = new PrimitiveTypeSymbol("hondalık");
        _mantıksal = new PrimitiveTypeSymbol("mantıksal");
        _nesne = new PrimitiveTypeSymbol("nesne");
        _dinamik = new PrimitiveTypeSymbol("dinamik");
        _tarih = new PrimitiveTypeSymbol("tarih");
        _saat = new PrimitiveTypeSymbol("saat");
        _tarihsaat = new PrimitiveTypeSymbol("tarihsaat");
        _süre = new PrimitiveTypeSymbol("süre");
        _root = new Scope(null);
        _scope = _root;
        foreach (var primitive in new[] { _metin, _karakter, _tamsayı, _utamsayı, _ondalık, _gondalık, _hondalık, _mantıksal, _nesne, _dinamik, _tarih, _saat, _tarihsaat, _süre })
        {
            _root.TryDeclare(primitive);
        }

        foreach (var generic in new[] { "liste", "dizi", "küme", "sliste" })
        {
            _root.TryDeclare(new GenericDefinitionSymbol(generic, 1));
        }

        _root.TryDeclare(new GenericDefinitionSymbol("sözlük", 2));
        _hata = new SourceTypeSymbol("Hata", SourceTypeKind.Class) { Visibility = Visibility.Public, BaseType = _nesne };
        _root.TryDeclare(_hata);
        DeclareErrorType();
        DeclareFunction("syaz", _metin, Intrinsic.WriteLine);
        DeclareFunction("yaz", _metin, Intrinsic.Write);
        DeclareRoot(Procedure("temizle", Intrinsic.Clear));
        DeclareRoot(Procedure("sor", Intrinsic.Ask, _metin, ("istem", _metin)));
        DeclareRoot(Procedure("oku", Intrinsic.ReadLine, _metin));
        DeclareLibrary();
        _root.TryDeclare(new VariableSymbol("bugün", _tarih));
        _root.TryDeclare(new VariableSymbol("şimdi", _tarihsaat));
        _flow = _flow.Add(_root.FindLocal("bugün")!).Add(_root.FindLocal("şimdi")!);
    }

    private readonly Dictionary<string, FunctionSymbol> _textMethods = new(StringComparer.Ordinal);

    private TypeSymbol _listText = null!;

    public static BoundCompilation Bind(SourceFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var parsed = global::Us.Parser.Parser.Parse(file);
        return Bind(file, parsed.Root);
    }

    public static BoundCompilation Bind(SourceFile file, CompilationUnitSyntax root)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(root);
        return new Binder(file).BindUnit(root);
    }

    public static BoundCompilation BindMany(IReadOnlyList<(SourceFile File, CompilationUnitSyntax Root)> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0)
        {
            throw new ArgumentException("En az bir derleme birimi gerekir.", nameof(units));
        }

        if (units.Count == 1)
        {
            return Bind(units[0].File, units[0].Root);
        }

        return new Binder(units[0].File).BindUnits(units);
    }

    private void DeclareErrorType()
    {
        var span = new TextSpan(0, 0);
        var message = new FieldSymbol("mesaj", _metin, Visibility.Public) { DeclaringType = _hata };
        _hata.Fields.Add(message);
        var empty = new FunctionSymbol("oluştur")
        {
            IsConstructor = true,
            DeclaringType = _hata,
            HasBody = true,
            ReturnType = UnitTypeSymbol.Instance,
            Visibility = Visibility.Public,
            BoundBody = new BoundNode(BoundKind.Block, span, UnitTypeSymbol.Instance, null, []),
        };
        var parameter = new ParameterSymbol("mesaj", _metin, false, false, false);
        var textual = new FunctionSymbol("oluştur")
        {
            IsConstructor = true,
            DeclaringType = _hata,
            HasBody = true,
            ReturnType = UnitTypeSymbol.Instance,
            Visibility = Visibility.Public,
        };
        textual.Parameters.Add(parameter);
        var assignment = BoundNode.Create(
            BoundKind.Assignment,
            span,
            _metin,
            message,
            false,
            BoundNode.Leaf(BoundKind.Name, span, _metin, message),
            BoundNode.Leaf(BoundKind.Name, span, _metin, parameter)).With(TokenKind.Equals);
        textual.BoundBody = new BoundNode(BoundKind.Block, span, UnitTypeSymbol.Instance, null, [assignment]);
        _hata.Methods.Add(empty);
        _hata.Methods.Add(textual);
    }

    private void DeclareFunction(string name, TypeSymbol parameterType, Intrinsic intrinsic)
    {
        DeclareRoot(Procedure(name, intrinsic, UnitTypeSymbol.Instance, ("değer", parameterType)));
    }

    private void DeclareRoot(FunctionSymbol function) => _root.TryDeclare(function);

    private static FunctionSymbol Procedure(string name, Intrinsic intrinsic, TypeSymbol? result = null, params (string Name, TypeSymbol Type)[] parameters)
    {
        var function = new FunctionSymbol(name)
        {
            ReturnType = result ?? UnitTypeSymbol.Instance,
            Visibility = Visibility.Public,
            HasBody = true,
            Intrinsic = intrinsic,
        };
        foreach (var parameter in parameters)
        {
            function.Parameters.Add(new ParameterSymbol(parameter.Name, parameter.Type, false, false, false));
        }

        return function;
    }

    private void DeclareLibrary()
    {
        _listText = new ConstructedTypeSymbol((GenericDefinitionSymbol)_root.FindLocal("liste")!, [_metin], false);
        Text("kırp", Intrinsic.TextTrim, _metin);
        Text("büyük", Intrinsic.TextUpper, _metin);
        Text("küçük", Intrinsic.TextLower, _metin);
        Text("içerir", Intrinsic.TextContains, _mantıksal, ("parça", _metin));
        Text("başlar", Intrinsic.TextStarts, _mantıksal, ("parça", _metin));
        Text("biter", Intrinsic.TextEnds, _mantıksal, ("parça", _metin));
        Text("değiştir", Intrinsic.TextReplace, _metin, ("eski", _metin), ("yeni", _metin));
        Text("böl", Intrinsic.TextSplit, _listText, ("ayraç", _metin));
        Text("birleştir", Intrinsic.TextJoin, _metin, ("değerler", _listText));
        Module("json",
            Procedure("yaz", Intrinsic.JsonWrite, _metin, ("değer", _nesne)),
            Procedure("oku", Intrinsic.JsonRead, _nesne, ("metin", _metin)));
        Module("dosya",
            Procedure("oku", Intrinsic.FileRead, _metin, ("yol", _metin)),
            Procedure("yaz", Intrinsic.FileWrite, parameters: [("yol", _metin), ("içerik", _metin)]),
            Procedure("sonaEkle", Intrinsic.FileAppend, parameters: [("yol", _metin), ("içerik", _metin)]),
            Procedure("sil", Intrinsic.FileDelete, parameters: [("yol", _metin)]),
            Procedure("varMı", Intrinsic.FileExists, _mantıksal, ("yol", _metin)));
        Module("klasör",
            Procedure("oluştur", Intrinsic.DirectoryCreate, parameters: [("yol", _metin)]),
            Procedure("sil", Intrinsic.DirectoryDelete, parameters: [("yol", _metin)]),
            Procedure("varMı", Intrinsic.DirectoryExists, _mantıksal, ("yol", _metin)),
            Procedure("dosyalar", Intrinsic.DirectoryFiles, _listText, ("yol", _metin)));
        Module("yol", Procedure("birleştir", Intrinsic.PathCombine, _metin, ("sol", _metin), ("sağ", _metin)));
        Module("log",
            Procedure("iz", Intrinsic.LogTrace, parameters: [("ileti", _metin)]),
            Procedure("bilgi", Intrinsic.LogInfo, parameters: [("ileti", _metin)]),
            Procedure("uyarı", Intrinsic.LogWarning, parameters: [("ileti", _metin)]),
            Procedure("hata", Intrinsic.LogError, parameters: [("ileti", _metin)]),
            Procedure("kritik", Intrinsic.LogCritical, parameters: [("ileti", _metin)]));
        _httpResultType = new HttpResultTypeSymbol();
        _httpBuilderType = new HttpBuilderTypeSymbol();
        _httpResponseType = new HttpResponseTypeSymbol();
        DeclareRoot(HttpHelper("tamam", Intrinsic.HttpOk));
        DeclareRoot(HttpHelper("oluşturuldu", Intrinsic.HttpCreated));
        DeclareRoot(Procedure("içerikYok", Intrinsic.HttpNoContent, _nesne));
        DeclareRoot(HttpHelper("hatalıİstek", Intrinsic.HttpBadRequest));
        DeclareRoot(HttpHelper("yetkisiz", Intrinsic.HttpUnauthorized));
        DeclareRoot(HttpHelper("yasak", Intrinsic.HttpForbidden));
        DeclareRoot(HttpHelper("bulunamadı", Intrinsic.HttpNotFound));
        DeclareRoot(HttpHelper("çakışma", Intrinsic.HttpConflict));
        Module("http",
            Procedure("GET", Intrinsic.HttpGet, _httpResponseType, ("url", _metin)),
            HttpVerb("POST", Intrinsic.HttpPost),
            HttpVerb("PUT", Intrinsic.HttpPut),
            HttpVerb("PATCH", Intrinsic.HttpPatch),
            Procedure("DELETE", Intrinsic.HttpDelete, _httpResponseType, ("url", _metin)),
            Procedure("başlık", Intrinsic.HttpHeader, _httpBuilderType, ("ad", _metin), ("değer", _metin)));
    }

    private FunctionSymbol HttpHelper(string name, Intrinsic intrinsic)
    {
        var function = new FunctionSymbol(name) { Intrinsic = intrinsic, ReturnType = _nesne };
        function.Parameters.Add(new ParameterSymbol("gövde", _dinamik, false, false, true)
        {
            Containing = function,
            Default = BoundNode.Leaf(BoundKind.Literal, default, NullTypeSymbol.Instance),
        });
        return function;
    }

    private FunctionSymbol HttpVerb(string name, Intrinsic intrinsic)
    {
        var function = new FunctionSymbol(name) { Intrinsic = intrinsic, ReturnType = _httpResponseType! };
        function.Parameters.Add(new ParameterSymbol("url", _metin, false, false, false) { Containing = function });
        function.Parameters.Add(new ParameterSymbol("gövde", _dinamik, false, false, true)
        {
            Containing = function,
            Default = BoundNode.Leaf(BoundKind.Literal, default, NullTypeSymbol.Instance),
        });
        return function;
    }

    private void Text(string name, Intrinsic intrinsic, TypeSymbol result, params (string Name, TypeSymbol Type)[] parameters) =>
        _textMethods[name] = Procedure(name, intrinsic, result, parameters);

    private void Module(string name, params FunctionSymbol[] methods)
    {
        var module = new ModuleSymbol(name);
        module.Methods.AddRange(methods);
        _root.TryDeclare(module);
    }

    private BoundCompilation BindUnit(CompilationUnitSyntax root) =>
        BindUnits([( _file, root )]);

    private BoundCompilation BindUnits(IReadOnlyList<(SourceFile File, CompilationUnitSyntax Root)> units)
    {
        foreach (var unit in units)
        {
            _file = unit.File;
            _filePublic = false;
            foreach (var member in unit.Root.Members)
            {
                DeclareTop(member);
            }
        }

        foreach (var type in _types)
        {
            if (_typeFiles.TryGetValue(type, out var typeFile))
            {
                _file = typeFile;
            }

            ResolveType(type);
        }

        CheckHierarchies();
        foreach (var pair in _databaseSyntax)
        {
            FillTables(pair.Key, pair.Value);
        }

        var members = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var unit in units)
        {
            _file = unit.File;
            _filePublic = false;
            foreach (var member in unit.Root.Members)
            {
                members.Add(BindTop(member));
            }
        }

        foreach (var pending in _functions)
        {
            _file = pending.File;
            BindFunctionBody(pending.Symbol, pending.Syntax, pending.Scope);
        }

        foreach (var pending in _constructors)
        {
            _file = pending.File;
            BindConstructorBody(pending.Symbol, pending.Syntax, pending.Scope);
        }

        RegisterServices();
        foreach (var pending in _controllerBodies)
        {
            _file = pending.File;
            BindControllerSetup(pending.Syntax, pending.Symbol, pending.Scope);
        }

        var functions = _functions.Select(item => item.Symbol)
            .Concat(_constructors.Select(item => item.Symbol))
            .Distinct()
            .ToImmutableArray();
        return new BoundCompilation(
            members.ToImmutable(),
            _diagnostics,
            _tests.ToImmutableArray(),
            _services.ToImmutableArray(),
            _databases.ToImmutableArray(),
            _controllers.ToImmutableArray(),
            _routes.ToImmutableArray(),
            functions);
    }

    private void BindControllerSetup(ControllerDeclarationSyntax syntax, ControllerSymbol symbol, Scope scope)
    {
        var previous = _scope;
        var previousFlow = _flow;
        _scope = scope;
        _flow = Flow.Empty;
        foreach (var member in syntax.Members)
        {
            if (member is BindingStatementSyntax binding)
            {
                var kind = Text(binding.KindName);
                if (kind is not ("servis" or "db"))
                {
                    continue;
                }

                var existing = scope.FindLocal(kind) as VariableSymbol;
                if (existing is null)
                {
                    continue;
                }

                _flow = _flow.Add(existing);
                symbol.Setup.Add(BoundNode.Leaf(BoundKind.Local, binding.Span, existing.Type, existing, constant: kind));
            }
        }

        _scope = previous;
        _flow = previousFlow;
    }

    private void DeclareTop(SyntaxNode node)
    {
        switch (node)
        {
            case FunctionDeclarationSyntax function:
                var symbol = CreateFunction(function, null, _filePublic ? Visibility.Public : Visibility.Internal);
                if (Declare(symbol, function.Name.Span))
                {
                    _functions.Add((symbol, function, _root, _file));
                }

                break;
            case ModelDeclarationSyntax model:
                DeclareType(model.Name, SourceTypeKind.Model, model.TypeParameters, Visibility.Public, model);
                break;
            case ClassDeclarationSyntax type:
                DeclareType(type.Name, SourceTypeKind.Class, type.TypeParameters, VisibilityOf(type.Modifiers, false), type);
                break;
            case InterfaceDeclarationSyntax face:
                DeclareType(face.Name, SourceTypeKind.Interface, null, Visibility.Public, face);
                break;
            case OptionDeclarationSyntax option:
                DeclareType(option.Name, SourceTypeKind.Option, null, Visibility.Public, option);
                break;
            case ServiceDeclarationSyntax service:
                DeclareType(service.Type.Name, SourceTypeKind.Class, null, Visibility.Public, service);
                break;
            case DatabaseDeclarationSyntax database:
                var stored = new DatabaseSymbol(Text(database.Name));
                Declare(stored, database.Name.Span);
                _databases.Add(stored);
                _databaseSyntax[database] = stored;
                break;
            case ControllerDeclarationSyntax controller:
                DeclareController(controller);
                break;
        }
    }

    private void DeclareController(ControllerDeclarationSyntax controller)
    {
        var prefix = DecodeString(Text(controller.Route));
        var symbol = new ControllerSymbol(Text(controller.Name), prefix, controller.Name.Span);
        if (!Declare(symbol, controller.Name.Span))
        {
            return;
        }

        _controllers.Add(symbol);
        var scope = new Scope(_scope);
        _controllerBodies.Add((controller, symbol, scope, _file));
        var previous = _scope;
        _scope = scope;
        foreach (var member in controller.Members)
        {
            if (member is BindingStatementSyntax binding)
            {
                var kind = Text(binding.KindName);
                if (kind == "servis")
                {
                    var serviceType = ResolveType(binding.Type);
                    scope.TryDeclare(new VariableSymbol("servis", serviceType));
                }
                else if (kind == "db")
                {
                    var name = Text(binding.Type.Name);
                    if (_root.Lookup(name).OfType<DatabaseSymbol>().FirstOrDefault() is DatabaseSymbol database)
                    {
                        scope.TryDeclare(new VariableSymbol("db", new DatabaseTypeSymbol(database)));
                    }
                }
            }
            else if (member is FunctionDeclarationSyntax function)
            {
                var handler = CreateFunction(function, null, Visibility.Public);
                if (scope.TryDeclare(handler))
                {
                    _functions.Add((handler, function, scope, _file));
                    if (function.Route is not null)
                    {
                        RegisterRoute(symbol, function.Route, handler);
                    }
                }
            }
        }

        _scope = previous;
    }

    private void RegisterRoute(ControllerSymbol controller, RouteDeclarationSyntax route, FunctionSymbol handler)
    {
        var verb = Text(route.Verb);
        if (verb is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
        {
            Error(DiagnosticCodes.UnknownHttpVerb, $"'{verb}' HTTP fiili desteklenmiyor.", route.Verb.Span);
            return;
        }

        string relative;
        try
        {
            relative = RoutePathText(route.Path);
        }
        catch (InvalidOperationException)
        {
            Error(DiagnosticCodes.UnexpectedToken, "Rota yolu metin olmalıdır.", route.Path.Span);
            return;
        }

        var template = CombineRoute(controller.Prefix, relative);
        var structural = StructuralRoute(verb, template);
        if (_routes.Any(item => item.StructuralKey == structural))
        {
            Error(DiagnosticCodes.DuplicateRoute, $"'{verb} {template}' rotası yineleniyor.", route.Keyword.Span);
            return;
        }

        var symbol = new RouteSymbol(verb, template, structural, controller, handler, route.Keyword.Span);
        foreach (var name in symbol.Parameters)
        {
            if (handler.Parameters.All(parameter => parameter.Name != name))
            {
                Error(DiagnosticCodes.UnresolvedName, $"'{name}' rota parametresi işleyicide yok.", route.Path.Span);
            }
        }

        controller.Routes.Add(symbol);
        _routes.Add(symbol);
    }

    private string RoutePathText(SyntaxNode path)
    {
        if (path is SyntaxToken token && token.TokenKind == TokenKind.StringLiteral)
        {
            return DecodeString(Text(token));
        }

        var raw = _file.Text.ToString().Substring(path.Span.Start, path.Span.Length);
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            return DecodeString(raw);
        }

        throw new InvalidOperationException();
    }

    private static string CombineRoute(string prefix, string relative)
    {
        prefix = prefix.TrimEnd('/');
        if (relative.Length == 0 || relative == "/")
        {
            return prefix.Length == 0 ? "/" : prefix;
        }

        if (!relative.StartsWith('/'))
        {
            relative = "/" + relative;
        }

        return prefix + relative;
    }

    private static string StructuralRoute(string method, string template)
    {
        var parts = template.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}' ? "{}" : segment);
        return method + "|" + string.Join('/', parts);
    }

    private void DeclareType(SyntaxToken name, SourceTypeKind kind, TypeParameterListSyntax? parameters, Visibility visibility, SyntaxNode syntax)
    {
        var type = new SourceTypeSymbol(Text(name), kind) { Visibility = visibility };
        type.IsAbstract = syntax is ClassDeclarationSyntax cls && cls.Modifiers.Any(token => token.TokenKind == TokenKind.Soyut);
        type.IsSealed = syntax is ClassDeclarationSyntax sealedType && sealedType.Modifiers.Any(token => token.TokenKind == TokenKind.Muhurlu);
        if (syntax is ModelDeclarationSyntax configured && configured.SettingName is not null)
        {
            if (Text(configured.SettingName) != "Ayar" || configured.SettingArgument is null)
            {
                Error(DiagnosticCodes.TypeMismatch, "Ayar kalıtım değildir. Bölüm Ayar(\"...\") olarak yazılır.", configured.SettingName.Span);
            }
            else
            {
                type.ConfigSection = DecodeString(Text(configured.SettingArgument));
            }
        }
        if (parameters is not null)
        {
            foreach (var parameter in parameters.Parameters)
            {
                var typeParameter = new TypeParameterSymbol(Text(parameter.Name));
                type.TypeParameters.Add(typeParameter);
            }
        }

        if (Declare(type, name.Span))
        {
            _types.Add(type);
            _typeNodes[type] = syntax;
            _typeFiles[type] = _file;
        }
    }

    private readonly Dictionary<SourceTypeSymbol, SyntaxNode> _typeNodes = [];

    private readonly Dictionary<SourceTypeSymbol, SourceFile> _typeFiles = [];

    private void ResolveType(SourceTypeSymbol type)
    {
        if (!_typeNodes.TryGetValue(type, out var node))
        {
            return;
        }

        var previous = _scope;
        _scope = new Scope(_root);
        foreach (var parameter in type.TypeParameters)
        {
            _scope.TryDeclare(parameter);
        }

        _currentType = type;
        switch (node)
        {
            case ModelDeclarationSyntax model:
                BindTypeParameters(type, model.TypeParameters);
                foreach (var field in model.Fields)
                {
                    AddFields(type, field.Names, field.Type, field.Modifiers, Visibility.Public, field);
                }

                break;
            case ClassDeclarationSyntax cls:
                BindTypeParameters(type, cls.TypeParameters);
                BindBases(type, cls.Bases);
                foreach (var member in cls.Members)
                {
                    DeclareMember(type, member);
                }

                break;
            case InterfaceDeclarationSyntax face:
                foreach (var member in face.Members)
                {
                    if (member is FunctionDeclarationSyntax function)
                    {
                        var symbol = CreateFunction(function, type, Visibility.Public);
                        symbol.IsAbstract = true;
                        type.Methods.Add(symbol);
                    }
                }

                break;
            case OptionDeclarationSyntax option:
                foreach (var member in option.Members)
                {
                    var item = new VariableSymbol(Text(member), type);
                    if (type.OptionMembers.Any(existing => existing.Name == item.Name))
                    {
                        Error(DiagnosticCodes.DuplicateOption, $"'{item.Name}' seçeneği yineleniyor.", member.Span);
                    }
                    else
                    {
                        type.OptionMembers.Add(item);
                    }
                }

                break;
            case ServiceDeclarationSyntax service:
                type.ServiceLifetime = ReadLifetime(service);
                foreach (var member in service.Members)
                {
                    DeclareMember(type, member);
                }

                break;
        }

        _currentType = null;
        _scope = previous;
    }

    private void BindTypeParameters(SourceTypeSymbol type, TypeParameterListSyntax? list)
    {
        if (list is null)
        {
            return;
        }

        for (var i = 0; i < list.Parameters.Length && i < type.TypeParameters.Count; i++)
        {
            foreach (var constraint in list.Parameters[i].Constraints)
            {
                var resolved = ResolveType(constraint);
                if (resolved is ErrorTypeSymbol)
                {
                    continue;
                }

                if (resolved is not SourceTypeSymbol source || source.TypeKind is not (SourceTypeKind.Class or SourceTypeKind.Interface))
                {
                    Error(DiagnosticCodes.ConstraintFailed, "Kısıt sınıf veya arayüz olmalıdır.", constraint.Span);
                }

                type.TypeParameters[i].Constraints.Add(resolved);
            }
        }
    }

    private void BindBases(SourceTypeSymbol type, ImmutableArray<TypeSyntax> bases)
    {
        var classCount = 0;
        foreach (var baseSyntax in bases)
        {
            var resolved = ResolveType(baseSyntax);
            if (resolved is not SourceTypeSymbol source)
            {
                if (resolved is not ErrorTypeSymbol)
                {
                    Error(DiagnosticCodes.MultipleClassBases, "Taban tip sınıf veya arayüz olmalıdır.", baseSyntax.Span);
                }

                continue;
            }

            source = source.Definition;
            if (source.TypeKind == SourceTypeKind.Class)
            {
                classCount++;
                if (classCount > 1)
                {
                    Error(DiagnosticCodes.MultipleClassBases, "Sınıfın birden çok sınıf tabanı olamaz.", baseSyntax.Span);
                }
                else if (source.IsSealed)
                {
                    Error(DiagnosticCodes.SealedBase, $"'{source.Name}' mühürlü olduğu için devralınamaz.", baseSyntax.Span);
                }
                else
                {
                    type.BaseType = source;
                }
            }
            else if (source.TypeKind == SourceTypeKind.Interface)
            {
                if (type.Interfaces.Any(item => Conversion.Same(item, source)))
                {
                    Error(DiagnosticCodes.DuplicateName, $"'{source.Name}' arayüzü yineleniyor.", baseSyntax.Span);
                }
                else
                {
                    type.Interfaces.Add(source);
                }
            }
            else
            {
                Error(DiagnosticCodes.MultipleClassBases, "Bu tip taban olarak kullanılamaz.", baseSyntax.Span);
            }
        }
    }

    private void DeclareMember(SourceTypeSymbol type, SyntaxNode member)
    {
        switch (member)
        {
            case FunctionDeclarationSyntax function:
                var symbol = CreateFunction(function, type, VisibilityOf(function.Modifiers, true));
                symbol.IsVirtual = function.Modifiers.Any(token => token.TokenKind == TokenKind.Sanal);
                symbol.IsOverride = function.Modifiers.Any(token => token.TokenKind == TokenKind.Yerinegec);
                symbol.IsAbstract = function.Modifiers.Any(token => token.TokenKind == TokenKind.Soyut) || function.Body is null && type.IsAbstract;
                symbol.IsStatic = function.Modifiers.Any(token => token.TokenKind == TokenKind.Statik);
                type.Methods.Add(symbol);
                _functions.Add((symbol, function, _scope, _file));
                break;
            case ConstructorDeclarationSyntax constructor:
                var created = new FunctionSymbol("oluştur")
                {
                    IsConstructor = true,
                    DeclaringType = type,
                    Visibility = VisibilityOf(constructor.Modifiers, true),
                    HasBody = true,
                    ReturnType = UnitTypeSymbol.Instance,
                };
                AddParameters(created, constructor.Parameters);
                type.Methods.Add(created);
                _constructors.Add((created, constructor, _scope, _file));
                break;
            case VariableDeclarationSyntax variable:
                AddFields(type, variable.Names, variable.Type, variable.Modifiers, Visibility.Private, variable);
                break;
            case EventDeclarationSyntax ev:
                var eventType = ResolveType(ev.Type);
                var eventSymbol = new EventSymbol(Text(ev.Name), eventType, VisibilityOf(ev.Modifiers, true)) { DeclaringType = type };
                type.Events.Add(eventSymbol);
                break;
        }
    }

    private void AddFields(SourceTypeSymbol type, ImmutableArray<SyntaxToken> names, TypeSyntax typeSyntax, ImmutableArray<SyntaxToken> modifiers, Visibility fallback, SyntaxNode owner)
    {
        var fieldType = ResolveType(typeSyntax);
        var visibility = VisibilityOf(modifiers, type.TypeKind != SourceTypeKind.Model);
        if (modifiers.Length == 0)
        {
            visibility = fallback;
        }

        var created = new List<FieldSymbol>();
        foreach (var name in names)
        {
            var field = new FieldSymbol(Text(name), fieldType, visibility) { DeclaringType = type };
            if (type.Fields.Any(existing => existing.Name == field.Name))
            {
                Error(DiagnosticCodes.DuplicateName, $"'{field.Name}' bu kapsamda zaten tanımlı.", name.Span);
            }
            else
            {
                type.Fields.Add(field);
                created.Add(field);
            }
        }

        if (owner is VariableDeclarationSyntax variable && variable.Initializers.Length > 0)
        {
            for (var index = 0; index < created.Count && index < variable.Initializers.Length; index++)
            {
                created[index].Default = BindExpression(variable.Initializers[index], false, fieldType);
            }
        }

        if (owner is ModelFieldSyntax fieldSyntax)
        {
            CheckConstraints(fieldSyntax, created);
        }
    }

    private void CheckConstraints(ModelFieldSyntax field, List<FieldSymbol> fields)
    {
        foreach (var constraint in field.Constraints)
        {
            var name = Text(constraint.Name);
            if (name is not ("anahtar" or "zorunlu" or "uzunluk" or "benzersiz" or "aralık" or "varsayılan"))
            {
                Error(DiagnosticCodes.UnknownConstraint, $"'{name}' bilinen bir kısıt değil.", constraint.Name.Span);
                continue;
            }

            if (name == "uzunluk" && !HasArgument(constraint))
            {
                Error(DiagnosticCodes.TypeMismatch, "uzunluk kısıtı tamsayı ister.", constraint.Span);
            }

            if (name == "aralık" && (constraint.Arguments is null || constraint.Arguments.Arguments.Length != 2))
            {
                Error(DiagnosticCodes.TypeMismatch, "aralık kısıtı iki sayı ister.", constraint.Span);
            }

            if (name == "zorunlu")
            {
                foreach (var item in fields)
                {
                    item.IsRequired = true;
                }
            }

            if (name == "benzersiz")
            {
                foreach (var item in fields)
                {
                    item.IsUnique = true;
                }
            }

            if (name == "anahtar")
            {
                foreach (var item in fields)
                {
                    item.IsKey = true;
                }
            }

            if (name == "uzunluk" && constraint.Arguments is { Arguments.Length: 1 })
            {
                var limit = BindExpression(constraint.Arguments.Arguments[0].Expression, false);
                Expect(DiagnosticCodes.TypeMismatch, limit.Type, _tamsayı, constraint.Span, "uzunluk kısıtı tamsayı ister.");
                foreach (var item in fields)
                {
                    item.MaxLength = limit;
                }
            }

            if (name == "aralık" && constraint.Arguments is { Arguments.Length: 2 })
            {
                var minimum = BindExpression(constraint.Arguments.Arguments[0].Expression, false);
                var maximum = BindExpression(constraint.Arguments.Arguments[1].Expression, false);
                if (!IsNumeric(minimum.Type) || !IsNumeric(maximum.Type))
                {
                    Error(DiagnosticCodes.TypeMismatch, "aralık kısıtı iki sayı ister.", constraint.Span);
                }

                foreach (var item in fields)
                {
                    item.RangeMinimum = minimum;
                    item.RangeMaximum = maximum;
                }
            }

            if (name == "varsayılan" && constraint.Arguments is { Arguments.Length: 1 })
            {
                var value = BindExpression(constraint.Arguments.Arguments[0].Expression, false);
                foreach (var item in fields)
                {
                    item.Default = value;
                }
            }
        }
    }

    private static bool HasArgument(ConstraintSyntax constraint) =>
        constraint.Arguments is { Arguments.Length: 1 };

    private void CheckHierarchies()
    {
        foreach (var type in _types)
        {
            var seen = new HashSet<SourceTypeSymbol>();
            for (var current = type.BaseType as SourceTypeSymbol; current is not null; current = current.BaseType as SourceTypeSymbol)
            {
                current = current.Definition;
                if (!seen.Add(current) || ReferenceEquals(current, type))
                {
                    Error(DiagnosticCodes.InheritanceCycle, $"'{type.Name}' kalıtım döngüsü oluşturuyor.", new TextSpan(0, 0));
                    type.BaseType = null;
                    break;
                }
            }

            if (type.TypeKind == SourceTypeKind.Class && !type.IsAbstract)
            {
                foreach (var required in RequiredMethods(type))
                {
                    if (!type.Methods.Any(method => SameSignature(method, required)))
                    {
                        Error(DiagnosticCodes.MissingImplementation, $"'{type.Name}' '{required.Name}' üyesini uygulamıyor.", new TextSpan(0, 0));
                    }
                }
            }

            foreach (var method in type.Methods.Where(method => method.IsOverride))
            {
                var target = FindOverride(type, method);
                if (target is null)
                {
                    Error(DiagnosticCodes.BadOverride, $"'{method.Name}' için yerinegeç hedefi yok.", new TextSpan(0, 0));
                }
                else if (target.DeclaringType?.IsSealed == true)
                {
                    Error(DiagnosticCodes.SealedTarget, $"'{method.Name}' mühürlü hedefi eziyor.", new TextSpan(0, 0));
                }
                else
                {
                    method.OverrideOf = target;
                    method.Targets.Add(target);
                    method.Targets.AddRange(target.Targets);
                }
            }

            if (type.TypeKind == SourceTypeKind.Class && type.BaseType is SourceTypeSymbol baseType && !HasParameterlessConstructor(baseType.Definition))
            {
                Error(DiagnosticCodes.MissingBody, $"'{type.Name}' tabanı parametresiz oluştur ister. temel(...) sözdizimi tanımlı değil.", new TextSpan(0, 0));
            }

            LinkInterfaces(type);
        }
    }

    private static bool HasParameterlessConstructor(SourceTypeSymbol type)
    {
        var constructors = type.Methods.Where(method => method.IsConstructor).ToList();
        return constructors.Count == 0 || constructors.Any(method => method.Parameters.All(parameter => parameter.HasDefault));
    }

    private static void LinkInterfaces(SourceTypeSymbol type)
    {
        foreach (var face in type.Interfaces.OfType<SourceTypeSymbol>())
        {
            foreach (var required in face.Definition.Methods)
            {
                var implementation = FindImplementation(type, required);
                if (implementation is not null && !implementation.Targets.Contains(required))
                {
                    implementation.Targets.Add(required);
                }
            }
        }
    }

    private static FunctionSymbol? FindImplementation(SourceTypeSymbol type, FunctionSymbol required)
    {
        var seen = new HashSet<SourceTypeSymbol>();
        for (var current = type; current is not null && seen.Add(current.Definition); current = current.Definition.BaseType as SourceTypeSymbol)
        {
            var found = current.Definition.Methods.FirstOrDefault(method => SameSignature(method, required) && !method.IsConstructor && !method.IsAbstract);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private IEnumerable<FunctionSymbol> RequiredMethods(SourceTypeSymbol type)
    {
        var current = type.BaseType as SourceTypeSymbol;
        var seen = new HashSet<SourceTypeSymbol>();
        while (current is not null && seen.Add(current.Definition))
        {
            foreach (var method in current.Definition.Methods.Where(method => method.IsAbstract))
            {
                yield return method;
            }

            current = current.Definition.BaseType as SourceTypeSymbol;
        }

        foreach (var face in type.Interfaces.OfType<SourceTypeSymbol>())
        {
            foreach (var method in face.Definition.Methods)
            {
                yield return method;
            }
        }
    }

    private static bool SameSignature(FunctionSymbol left, FunctionSymbol right) =>
        left.Name == right.Name && left.Parameters.Count == right.Parameters.Count;

    private FunctionSymbol? FindOverride(SourceTypeSymbol type, FunctionSymbol method)
    {
        var current = type.BaseType as SourceTypeSymbol;
        var seen = new HashSet<SourceTypeSymbol>();
        while (current is not null && seen.Add(current.Definition))
        {
            var found = current.Definition.Methods.FirstOrDefault(candidate => SameSignature(candidate, method) && (candidate.IsVirtual || candidate.IsAbstract));
            if (found is not null)
            {
                return found;
            }

            current = current.Definition.BaseType as SourceTypeSymbol;
        }

        return null;
    }

    private BoundNode BindTop(SyntaxNode node)
    {
        _awaitAllowed = true;
        _currentType = null;
        _function = null;
        return node switch
        {
            FunctionDeclarationSyntax => BoundNode.Leaf(BoundKind.Other, node.Span, UnitTypeSymbol.Instance),
            ModelDeclarationSyntax or ClassDeclarationSyntax or InterfaceDeclarationSyntax or OptionDeclarationSyntax => BoundNode.Leaf(BoundKind.Type, node.Span, UnitTypeSymbol.Instance),
            NamespaceDeclarationSyntax space => BindNamespace(space),
            _ => BindStatement(node),
        };
    }

    private BoundNode BindNamespace(NamespaceDeclarationSyntax space)
    {
        _filePublic = space.Visibility?.TokenKind == TokenKind.Genel;
        return BoundNode.Leaf(BoundKind.Other, space.Span, UnitTypeSymbol.Instance);
    }

    private BoundNode BindStatement(SyntaxNode node)
    {
        switch (node)
        {
            case EmptyStatementSyntax:
                return BoundNode.Leaf(BoundKind.Other, node.Span, UnitTypeSymbol.Instance);
            case BlockSyntax block:
                return BindBlock(block, true);
            case VariableDeclarationSyntax variable:
                return BindVariable(variable);
            case ExpressionStatementSyntax expression:
                if (expression.Expression is IdentifierExpressionSyntax identifier && _root.FindLocal(Text(identifier.Name)) is FunctionSymbol bare && bare.Intrinsic != Intrinsic.None && bare.Parameters.Count == 0)
                {
                    return new BoundNode(BoundKind.Call, expression.Span, UnitTypeSymbol.Instance, bare, []);
                }

                return BoundNode.Create(BoundKind.Expression, expression.Span, UnitTypeSymbol.Instance, null, false, BindExpression(expression.Expression, false));
            case ResultStatementSyntax result:
                return BindResult(result);
            case IfStatementSyntax ifStatement:
                return BindIf(ifStatement);
            case ForEachStatementSyntax each:
                return BindForEach(each);
            case RangeForStatementSyntax range:
                return BindRange(range);
            case WhileStatementSyntax wh:
                return BindWhile(wh.Keyword.Span, wh.Condition, wh.Body);
            case DoWhileStatementSyntax done:
                return BindDo(done);
            case InfiniteLoopStatementSyntax loop:
                return BindLoopBody(loop.Span, loop.Body, _mantıksal).With(TokenKind.Sonsuzdon);
            case BreakStatementSyntax:
                if (_loopDepth == 0)
                {
                    Error(DiagnosticCodes.BadJump, "çık ve devam yalnız döngü içinde yazılabilir.", node.Span);
                }

                return BoundNode.Leaf(BoundKind.Jump, node.Span, UnitTypeSymbol.Instance, op: TokenKind.Cik);
            case ContinueStatementSyntax:
                if (_loopDepth == 0)
                {
                    Error(DiagnosticCodes.BadJump, "çık ve devam yalnız döngü içinde yazılabilir.", node.Span);
                }

                return BoundNode.Leaf(BoundKind.Jump, node.Span, UnitTypeSymbol.Instance, op: TokenKind.Devam);
            case FunctionDeclarationSyntax:
                return BoundNode.Leaf(BoundKind.Other, node.Span, UnitTypeSymbol.Instance);
            case TryStatementSyntax tried:
                return BindTry(tried);
            case ThrowStatementSyntax thrown:
                return BindThrow(thrown);
            case SelectStatementSyntax select:
                return BindSelect(select);
            case ConcurrentStatementSyntax concurrent:
                return BindWithAwait(concurrent.Body, true).With(TokenKind.Eesislem);
            case TransactionStatementSyntax transaction:
                return BindTransaction(transaction);
            case CommitStatementSyntax commit:
                return BindTransactionExit(commit, TokenKind.Onayla);
            case RollbackStatementSyntax rollback:
                return BindTransactionExit(rollback, TokenKind.Gerial);
            case TestDeclarationSyntax test:
                return BindTest(test);
            case ExpectedErrorStatementSyntax expected:
                return BindWithAwait(expected.Body, true).With(constant: "beklenenHata");
            case AssertionStatementSyntax assertion:
                var condition = BindExpression(assertion.Expression, false);
                Expect(DiagnosticCodes.ExpectedType, condition.Type, _mantıksal, assertion.Expression.Span, "beklenen ifadesi mantıksal olmalıdır.");
                return BoundNode.Create(BoundKind.Expression, assertion.Span, UnitTypeSymbol.Instance, null, false, condition).With(TokenKind.Beklenen);
            case ValidationStatementSyntax validation:
                return BindValidation(validation);
            case MappingDeclarationSyntax mapping:
                return BindMapping(mapping);
            case ImportDeclarationSyntax import:
                return BindImport(import);
            case DatabaseDeclarationSyntax database:
                return BindDatabase(database);
            case ServiceDeclarationSyntax service:
                return BindService(service);
            case BindingStatementSyntax binding:
                return BindBinding(binding);
            case ControllerDeclarationSyntax controller:
                return BoundNode.Leaf(BoundKind.Other, controller.Span, UnitTypeSymbol.Instance, _controllers.FirstOrDefault(item => item.Name == Text(controller.Name)));
            case EventDeclarationSyntax ev:
                ResolveType(ev.Type);
                return BoundNode.Leaf(BoundKind.Other, ev.Span, UnitTypeSymbol.Instance);
            case ErrorStatementSyntax:
                return BoundNode.Leaf(BoundKind.Error, node.Span, ErrorTypeSymbol.Instance);
            default:
                Error(DiagnosticCodes.UnknownSyntax, "Bu sözdizimi bağlanamadı.", node.Span);
                return BoundNode.Leaf(BoundKind.Error, node.Span, ErrorTypeSymbol.Instance);
        }
    }

    private BoundNode BindBlock(BlockSyntax block, bool nestedScope)
    {
        var previous = _scope;
        if (nestedScope)
        {
            _scope = new Scope(previous);
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var statement in block.Statements)
        {
            children.Add(BindStatement(statement));
        }

        _scope = previous;
        return new BoundNode(BoundKind.Block, block.Span, UnitTypeSymbol.Instance, null, children.ToImmutable());
    }

    private BoundNode BindTest(TestDeclarationSyntax test)
    {
        var name = string.Empty;
        if (test.Name is LiteralExpressionSyntax literal && literal.Token.TokenKind == TokenKind.StringLiteral)
        {
            name = DecodeString(Text(literal.Token));
        }
        else
        {
            Error(DiagnosticCodes.TestName, "Test adı metin literali olmalıdır.", test.Name.Span);
        }

        if (name.Length > 0 && _tests.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
        {
            Error(DiagnosticCodes.DuplicateName, $"'{name}' testi bu kapsamda zaten tanımlı.", test.Name.Span);
        }

        var body = BindWithAwait(test.Body, true);
        var symbol = new TestSymbol(name, test.Span, body, ContainsAwait(body));
        _tests.Add(symbol);
        return BoundNode.Leaf(BoundKind.Other, test.Span, UnitTypeSymbol.Instance, symbol);
    }

    private static bool ContainsAwait(BoundNode node)
    {
        if (node.Operator == TokenKind.Bekle)
        {
            return true;
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

    private BoundNode BindWithAwait(BlockSyntax block, bool allow)
    {
        var previous = _awaitAllowed;
        _awaitAllowed = allow;
        var bound = BindBlock(block, true);
        _awaitAllowed = previous;
        return bound;
    }

    private BoundNode BindVariable(VariableDeclarationSyntax variable)
    {
        var type = ResolveType(variable.Type);
        if (variable.EqualsToken is not null && variable.Names.Length != variable.Initializers.Length)
        {
            Error(DiagnosticCodes.InitializerCount, "Ad sayısı ile değer sayısı eşit olmalıdır.", variable.Span);
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        for (var i = 0; i < variable.Names.Length; i++)
        {
            var name = variable.Names[i];
            BoundNode? initializer = null;
            if (i < variable.Initializers.Length)
            {
                initializer = BindExpression(variable.Initializers[i], false, type);
                if (initializer.Type is NullTypeSymbol && !type.IsNullable)
                {
                    Error(DiagnosticCodes.NullNotAllowed, $"'{Text(name)}' boş değer alamaz.", variable.Initializers[i].Span);
                }
                else if (!Conversion.IsImplicit(initializer.Type, type))
                {
                    var suggestion = Suggestion(variable.Initializers[i]);
                    Error(DiagnosticCodes.InitializerMismatch, $"'{Text(name)}' değişkeninin türü '{type.DisplayName}', ancak verilen değer '{initializer.Type.DisplayName}'.", variable.Initializers[i].Span, suggestion);
                }
            }

            var symbol = new VariableSymbol(Text(name), type);
            if (initializer?.Symbol is FunctionSymbol callable && callable.IsLambda)
            {
                symbol.Callable = callable;
            }

            DeclareValue(symbol, name.Span);
            if (initializer is not null)
            {
                _flow = _flow.Add(symbol);
            }

            children.Add(BoundNode.Create(BoundKind.Local, name.Span, type, symbol, false, initializer));
        }

        return new BoundNode(BoundKind.Local, variable.Span, type, null, children.ToImmutable());
    }

    private BoundNode BindIf(IfStatementSyntax statement)
    {
        var condition = BindCondition(statement.Condition);
        var before = _flow;
        var thenNode = BindStatement(statement.Body);
        var thenFlow = _flow;
        _flow = before;
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(condition);
        children.Add(thenNode);
        foreach (var clause in statement.ElseIfs)
        {
            children.Add(BindCondition(clause.Condition));
            _flow = before;
            children.Add(BindStatement(clause.Body));
            thenFlow = Merge(_flow, thenFlow);
            _flow = before;
        }

        BoundNode? elseNode = null;
        if (statement.ElseBody is not null)
        {
            elseNode = BindStatement(statement.ElseBody);
            children.Add(elseNode);
            _flow = Merge(thenFlow, _flow);
        }
        else
        {
            _flow = before;
        }

        return new BoundNode(BoundKind.If, statement.Span, UnitTypeSymbol.Instance, null, children.ToImmutable(), false, TokenKind.Eger);
    }

    private BoundNode BindCondition(ExpressionSyntax expression)
    {
        var bound = BindExpression(expression, false);
        if (bound.Type is not ErrorTypeSymbol && !Conversion.Same(bound.Type, _mantıksal))
        {
            Error(DiagnosticCodes.ConditionType, "Koşul mantıksal olmalıdır.", expression.Span);
        }

        return bound;
    }

    private BoundNode BindForEach(ForEachStatementSyntax statement)
    {
        var collection = BindExpression(statement.Collection, false);
        var element = ElementType(collection.Type) ?? ErrorTypeSymbol.Instance;
        if (element is ErrorTypeSymbol && collection.Type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.BinaryType, "dön koleksiyonunun öğe tipi yok.", statement.Collection.Span);
        }

        return BindIteration(statement.Span, statement.Name, element, statement.Body, collection);
    }

    private BoundNode BindRange(RangeForStatementSyntax statement)
    {
        var start = BindExpression(statement.Start, false);
        var end = BindExpression(statement.End, false);
        Expect(DiagnosticCodes.BinaryType, start.Type, _tamsayı, statement.Start.Span, "Aralık ucu tamsayı olmalıdır.");
        Expect(DiagnosticCodes.BinaryType, end.Type, _tamsayı, statement.End.Span, "Aralık ucu tamsayı olmalıdır.");
        BoundNode? step = null;
        if (statement.Step is not null)
        {
            step = BindExpression(statement.Step, false);
            Expect(DiagnosticCodes.BinaryType, step.Type, _tamsayı, statement.Step.Span, "Adım tamsayı olmalıdır.");
            if (step.Kind == BoundKind.Literal && int.TryParse(step.Constant, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) && value <= 0)
            {
                Error(DiagnosticCodes.LoopStep, "Adım sıfırdan büyük olmalıdır.", statement.Step.Span);
            }
        }

        var previous = _scope;
        _scope = new Scope(previous);
        var symbol = new VariableSymbol(Text(statement.Name), _tamsayı);
        DeclareValue(symbol, statement.Name.Span);
        _flow = _flow.Add(symbol);
        var flow = _flow;
        _loopDepth++;
        var body = BindStatement(statement.Body);
        _loopDepth--;
        _flow = flow;
        _scope = previous;
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(start);
        children.Add(end);
        if (step is not null)
        {
            children.Add(step);
        }

        children.Add(body);
        return new BoundNode(BoundKind.Loop, statement.Span, UnitTypeSymbol.Instance, symbol, children.ToImmutable(), false, TokenKind.Don);
    }

    private BoundNode BindIteration(TextSpan span, SyntaxToken name, TypeSymbol type, StatementSyntax body, BoundNode collection)
    {
        var previous = _scope;
        _scope = new Scope(previous);
        var symbol = new VariableSymbol(Text(name), type);
        DeclareValue(symbol, name.Span);
        _flow = _flow.Add(symbol);
        var boundBody = BindLoopBody(span, body, type);
        _scope = previous;
        return BoundNode.Create(BoundKind.Loop, span, UnitTypeSymbol.Instance, symbol, false, collection, boundBody);
    }

    private BoundNode BindWhile(TextSpan span, ExpressionSyntax condition, StatementSyntax body)
    {
        var test = BindCondition(condition);
        return BindLoopBody(span, body, test.Type, test).With(TokenKind.Tekrarla);
    }

    private BoundNode BindDo(DoWhileStatementSyntax statement)
    {
        var body = BindLoopBody(statement.Span, statement.Body, _mantıksal);
        if (statement.Tekrarla.IsMissing)
        {
            return body;
        }

        var test = BindCondition(statement.Condition);
        return BoundNode.Create(BoundKind.Loop, statement.Span, UnitTypeSymbol.Instance, null, false, body, test).With(TokenKind.Yap);
    }

    private BoundNode BindLoopBody(TextSpan span, StatementSyntax body, TypeSymbol type, BoundNode? extra = null)
    {
        var flow = _flow;
        _loopDepth++;
        var bound = BindStatement(body);
        _loopDepth--;
        _flow = flow;
        return BoundNode.Create(BoundKind.Loop, span, type, null, false, extra, bound);
    }

    private BoundNode BindResult(ResultStatementSyntax result)
    {
        var value = BindExpression(result.Expression, false);
        if (_function is null || _function.ReturnType is UnitTypeSymbol)
        {
            Error(DiagnosticCodes.UnexpectedResult, "Değer döndürmeyen fonksiyonda => yazılamaz.", result.Span);
        }
        else
        {
            Expect(DiagnosticCodes.TypeMismatch, value.Type, _function.ReturnType, result.Expression.Span, "Sonuç ifadesi dönüş tipine uymuyor.");
            CheckOutputs(result.Span);
        }

        return BoundNode.Create(BoundKind.Result, result.Span, value.Type, null, false, value);
    }

    private BoundNode BindTry(TryStatementSyntax statement)
    {
        var preparation = statement.PreparationBody is null
            ? new BoundNode(BoundKind.Block, statement.Span, UnitTypeSymbol.Instance, null, [])
            : BindBlock(statement.PreparationBody, true).With(statement.PreparationIsUnderscore ? TokenKind.BasindaUnderscore : TokenKind.Basinda);
        var body = statement.Body is null
            ? new BoundNode(BoundKind.Block, statement.Span, UnitTypeSymbol.Instance, null, [])
            : BindBlock(statement.Body, true);
        var catches = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var clause in statement.Catches)
        {
            var previous = _scope;
            _scope = new Scope(previous);
            var untyped = clause.Type is null;
            var catchType = untyped ? _hata : ResolveType(clause.Type!);
            if (catchType is not ErrorTypeSymbol && !Conversion.Same(catchType, _hata) && !Conversion.Derives(catchType, _hata))
            {
                Error(DiagnosticCodes.ThrowType, "Yakalanan tip Hata tipinden gelmelidir.", clause.Type?.Span ?? clause.Span);
            }

            var symbol = new VariableSymbol(Text(clause.Name), catchType);
            DeclareValue(symbol, clause.Name.Span);
            _flow = _flow.Add(symbol);
            catches.Add(BoundNode.Create(BoundKind.Other, clause.Span, catchType, symbol, untyped, BindBlock(clause.Body, false)));
            _scope = previous;
        }

        var finalBody = statement.Finally is null
            ? new BoundNode(BoundKind.Block, statement.Span, UnitTypeSymbol.Instance, null, [])
            : BindBlock(statement.Finally, true);
        var clauses = new BoundNode(BoundKind.Block, statement.Span, UnitTypeSymbol.Instance, null, catches.ToImmutable());
        return new BoundNode(BoundKind.Other, statement.Span, UnitTypeSymbol.Instance, null, [preparation, body, finalBody, clauses], statement.PreparationIsUnderscore, TokenKind.Dene);
    }

    private BoundNode BindThrow(ThrowStatementSyntax statement)
    {
        var value = BindExpression(statement.Expression, false);
        if (value.Type is not ErrorTypeSymbol && !Conversion.Same(value.Type, _hata) && !Conversion.Derives(value.Type, _hata))
        {
            Error(DiagnosticCodes.ThrowType, "fırlat ifadesi Hata tipinden gelmelidir.", statement.Expression.Span);
        }

        return BoundNode.Create(BoundKind.Expression, statement.Span, UnitTypeSymbol.Instance, null, false, value).With(TokenKind.Firlat);
    }

    private BoundNode BindSelect(SelectStatementSyntax statement)
    {
        var value = BindExpression(statement.Expression, false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var arms = ImmutableArray.CreateBuilder<BoundNode>();
        arms.Add(value);
        foreach (var arm in statement.Arms)
        {
            if (arm.IsWildcard)
            {
                arms.Add(BoundNode.Leaf(BoundKind.Literal, arm.Span, value.Type, constant: "_"));
            }
            else
            {
                var pattern = BindExpression(arm.Pattern, false);
                if (!Conversion.IsImplicit(pattern.Type, value.Type) && !Conversion.IsImplicit(value.Type, pattern.Type) && pattern.Type is not ErrorTypeSymbol && value.Type is not ErrorTypeSymbol)
                {
                    Error(DiagnosticCodes.SelectArmType, "seç kolu konu tipine uymuyor.", arm.Pattern.Span);
                }

                var key = TextOf(arm.Pattern);
                if (!seen.Add(key))
                {
                    Error(DiagnosticCodes.DuplicateOption, "Aynı seç kolu yineleniyor.", arm.Pattern.Span);
                }

                arms.Add(pattern);
            }

            arms.Add(BindStatement(arm.Body));
        }

        return new BoundNode(BoundKind.Other, statement.Span, UnitTypeSymbol.Instance, null, arms.ToImmutable(), false, TokenKind.Sec);
    }

    private BoundNode BindTransaction(TransactionStatementSyntax statement)
    {
        if (_transactionDepth > 0)
        {
            Error(DiagnosticCodes.NestedTransaction, "İç içe işlem bu sürümde desteklenmiyor.", statement.Keyword.Span);
            return BindBlock(statement.Body, true);
        }

        _transactionDepth++;
        var closed = _transactionClosed;
        _transactionClosed = false;
        var body = BindBlock(statement.Body, true);
        _transactionDepth--;
        _transactionClosed = closed;
        return body.With(TokenKind.Islem);
    }

    private BoundNode BindTransactionExit(SyntaxNode node, TokenKind kind)
    {
        if (_transactionDepth == 0)
        {
            Error(DiagnosticCodes.TransactionContext, "onayla ve gerial yalnız işlem içinde yazılabilir.", node.Span);
        }
        else if (_transactionClosed)
        {
            Error(DiagnosticCodes.TransactionClosed, "Kapanmış işlemde yeniden onayla veya gerial yazılamaz.", node.Span);
        }
        else
        {
            _transactionClosed = true;
        }

        return BoundNode.Leaf(BoundKind.Other, node.Span, UnitTypeSymbol.Instance, op: kind);
    }

    private BoundNode BindValidation(ValidationStatementSyntax statement)
    {
        var target = BindExpression(statement.Expression, false);
        var rules = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var rule in statement.Rules)
        {
            var condition = BindExpression(rule.Condition, false);
            var message = BindExpression(rule.Message, false);
            if (condition.Type is not ErrorTypeSymbol && !Conversion.Same(condition.Type, _mantıksal))
            {
                Error(DiagnosticCodes.RuleType, "kural koşulu mantıksal olmalıdır.", rule.Condition.Span);
            }

            if (message.Type is not ErrorTypeSymbol && !Conversion.Same(message.Type, _metin))
            {
                Error(DiagnosticCodes.RuleType, "kural iletisi metin olmalıdır.", rule.Message.Span);
            }

            rules.Add(condition);
            rules.Add(message);
        }

        rules.Insert(0, target);
        return new BoundNode(BoundKind.Other, statement.Span, _listText, null, rules.ToImmutable(), false, TokenKind.Dogrula);
    }

    private BoundNode BindMapping(MappingDeclarationSyntax syntax)
    {
        var source = ResolveType(syntax.Source);
        var destination = ResolveType(syntax.Destination);
        var previous = _currentItem;
        _currentItem = source;
        var mapping = new MappingSymbol(source, destination);
        if (_mappings.Any(item => Conversion.Same(item.Source, source) && Conversion.Same(item.Destination, destination)))
        {
            Error(DiagnosticCodes.DuplicateMapping, "Bu kaynak ve hedef için dönüşüm zaten var.", syntax.Span);
        }
        else
        {
            _mappings.Add(mapping);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in syntax.Members)
        {
            var name = Text(member.Name);
            if (!seen.Add(name))
            {
                Error(DiagnosticCodes.DuplicateName, $"'{name}' dönüşümde yineleniyor.", member.Name.Span);
            }

            var field = FindMember(destination, name);
            var value = BindExpression(member.Value, false);
            if (field is null && destination is not ErrorTypeSymbol)
            {
                Error(DiagnosticCodes.UnknownMember, $"'{name}' hedef tipte yok.", member.Name.Span);
            }
            else if (field is FieldSymbol target)
            {
                Expect(DiagnosticCodes.TypeMismatch, value.Type, Close(target.Type, destination), member.Value.Span, "Dönüşüm değeri alan tipine uymuyor.");
                mapping.Assignments.Add((target, value));
            }
        }

        _currentItem = previous;
        return BoundNode.Leaf(BoundKind.Other, syntax.Span, destination, mapping);
    }

    private BoundNode BindImport(ImportDeclarationSyntax import)
    {
        var name = string.Join(".", import.Name.Select(Text));
        if (import.Alias is not null)
        {
            var alias = new VariableSymbol(Text(import.Alias), _nesne);
            DeclareValue(alias, import.Alias.Span);
        }
        else if (import.Name.Length > 0 && import.Name[0].Token.Kind == TokenKind.Identifier && _root.Lookup(Text(import.Name[0])).Count == 0)
        {
            Error(DiagnosticCodes.UnresolvedName, $"'{name}' içe aktarılamadı.", import.Name[0].Span);
        }

        return BoundNode.Leaf(BoundKind.Other, import.Span, UnitTypeSymbol.Instance);
    }

    private BoundNode BindDatabase(DatabaseDeclarationSyntax database)
    {
        if (!_databaseSyntax.TryGetValue(database, out var symbol))
        {
            symbol = new DatabaseSymbol(Text(database.Name));
            _databases.Add(symbol);
            _databaseSyntax[database] = symbol;
            FillTables(database, symbol);
        }

        return BoundNode.Leaf(BoundKind.Other, database.Span, UnitTypeSymbol.Instance, symbol);
    }

    private void FillTables(DatabaseDeclarationSyntax database, DatabaseSymbol symbol)
    {
        if (symbol.Tables.Count > 0)
        {
            return;
        }

        foreach (var table in database.Tables)
        {
            var model = ResolveType(table.Type);
            if (model is not SourceTypeSymbol source || source.Definition.TypeKind != SourceTypeKind.Model)
            {
                if (model is not ErrorTypeSymbol)
                {
                    Error(DiagnosticCodes.TypeMismatch, "Tablo bir model ister.", table.Type.Span);
                }

                continue;
            }

            var name = Text(table.Name);
            if (symbol.Tables.Any(item => item.Name == name))
            {
                Error(DiagnosticCodes.DuplicateName, $"'{name}' bu veritabanında zaten tanımlı.", table.Name.Span);
                continue;
            }

            symbol.Tables.Add(new TableSymbol(name, source.Definition, symbol));
        }
    }

    private BoundNode BindService(ServiceDeclarationSyntax service)
    {
        ResolveType(service.Type);
        return BoundNode.Leaf(BoundKind.Other, service.Span, UnitTypeSymbol.Instance);
    }

    private ServiceLifetime? ReadLifetime(ServiceDeclarationSyntax service)
    {
        if (service.Lifetime is null)
        {
            Error(DiagnosticCodes.MissingLifetime, "Servis ömrü kapsamlı, tekil veya geçici olmalıdır.", service.Type.Span);
            return null;
        }

        return service.Lifetime.TokenKind switch
        {
            TokenKind.Kapsamli => ServiceLifetime.Scoped,
            TokenKind.Tekil => ServiceLifetime.Singleton,
            TokenKind.Gecici => ServiceLifetime.Transient,
            _ => Missing(service),
        };
    }

    private ServiceLifetime? Missing(ServiceDeclarationSyntax service)
    {
        Error(DiagnosticCodes.MissingLifetime, "Servis ömrü kapsamlı, tekil veya geçici olmalıdır.", service.Lifetime?.Span ?? service.Type.Span);
        return null;
    }

    private void RegisterServices()
    {
        foreach (var type in _types)
        {
            if (type.ServiceLifetime is not ServiceLifetime lifetime)
            {
                continue;
            }

            var constructors = type.Methods.Where(method => method.IsConstructor).ToList();
            FunctionSymbol? constructor = null;
            if (constructors.Count > 1)
            {
                Error(DiagnosticCodes.AmbiguousConstructor, $"'{type.Name}' için birden fazla oluştur var.", _typeNodes[type].Span);
            }
            else if (constructors.Count == 1)
            {
                constructor = constructors[0];
            }

            if (lifetime == ServiceLifetime.Singleton && constructor is not null)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    if (ServiceOf(parameter.Type) is SourceTypeSymbol dependency && dependency.ServiceLifetime == ServiceLifetime.Scoped)
                    {
                        Error(DiagnosticCodes.CaptiveDependency, $"'{type.Name}' tekil olduğu için kapsamlı '{dependency.Name}' bağımlılığı alamaz.", _typeNodes[type].Span);
                    }
                }
            }

            if (constructors.Count <= 1)
            {
                _services.Add(new ServiceRegistration(type, lifetime, constructor, _typeNodes[type].Span));
            }
        }
    }

    private static SourceTypeSymbol? ServiceOf(TypeSymbol type) => type switch
    {
        SourceTypeSymbol source => source.Definition,
        ConstructedTypeSymbol constructed when constructed.Definition is SourceTypeSymbol source => source.Definition,
        _ => null,
    };

    private BoundNode BindBinding(BindingStatementSyntax binding)
    {
        var kind = Text(binding.KindName);
        if (kind == "db")
        {
            var name = Text(binding.Type.Name);
            if (_root.Lookup(name).OfType<DatabaseSymbol>().FirstOrDefault() is not DatabaseSymbol database)
            {
                Error(DiagnosticCodes.UnresolvedName, $"'{name}' veritabanı değil.", binding.Type.Span);
                return BoundNode.Leaf(BoundKind.Error, binding.Span, ErrorTypeSymbol.Instance);
            }

            var variable = new VariableSymbol("db", new DatabaseTypeSymbol(database));
            DeclareValue(variable, binding.KindName.Span);
            _flow = _flow.Add(variable);
            return BoundNode.Create(BoundKind.Local, binding.Span, variable.Type, variable, false).With(constant: "db");
        }

        var boundType = ResolveType(binding.Type);
        if (kind == "log")
        {
            Error(DiagnosticCodes.DuplicateName, "'log' standart kapsamdadır.", binding.KindName.Span);
            return BoundNode.Leaf(BoundKind.Error, binding.Span, ErrorTypeSymbol.Instance);
        }

        if (kind == "ayar")
        {
            if (boundType is not SourceTypeSymbol configured || configured.Definition.ConfigSection is null)
            {
                Error(DiagnosticCodes.TypeMismatch, "bağla ayar bir Ayar modeli ister.", binding.Type.Span);
            }

            return BoundNode.Leaf(BoundKind.Other, binding.Span, boundType, boundType is SourceTypeSymbol source ? source.Definition : boundType, constant: "ayar");
        }

        if (kind == "servis" && boundType is SourceTypeSymbol serviceType)
        {
            var variable = new VariableSymbol(kind, serviceType);
            DeclareValue(variable, binding.KindName.Span);
            _flow = _flow.Add(variable);
            var marker = BoundNode.Leaf(BoundKind.Other, binding.Span, serviceType, constant: "çöz");
            return BoundNode.Create(BoundKind.Local, binding.Span, serviceType, variable, false, marker).With(constant: kind);
        }

        return BoundNode.Leaf(BoundKind.Other, binding.Span, UnitTypeSymbol.Instance);
    }

    private void BindFunctionBody(FunctionSymbol symbol, FunctionDeclarationSyntax syntax, Scope parent)
    {
        if (!symbol.HasBody && symbol.DeclaringType?.TypeKind != SourceTypeKind.Interface && !symbol.IsAbstract)
        {
            Error(DiagnosticCodes.MissingBody, $"'{symbol.Name}' gövdesi yazılmalıdır.", syntax.Name.Span);
            return;
        }

        if (syntax.Body is null)
        {
            return;
        }

        var previousScope = _scope;
        var previousFlow = _flow;
        var previousFunction = _function;
        var previousType = _currentType;
        var previousAwait = _awaitAllowed;
        var previousStatic = _staticContext;
        _scope = new Scope(parent);
        foreach (var parameter in symbol.TypeParameters)
        {
            _scope.TryDeclare(parameter);
        }

        _flow = previousFlow;
        foreach (var parameter in symbol.Parameters)
        {
            _scope.TryDeclare(parameter);
            if (!parameter.IsOut)
            {
                _flow = _flow.Add(parameter);
            }
        }

        foreach (var name in new[] { "servis", "db" })
        {
            if (parent.FindLocal(name) is VariableSymbol injected)
            {
                _flow = _flow.Add(injected);
            }
        }

        _function = symbol;
        _currentType = symbol.DeclaringType;
        _staticContext = symbol.IsStatic;
        _awaitAllowed = symbol.IsAsync || symbol.DeclaringType is null && false;
        if (symbol.IsAsync)
        {
            _awaitAllowed = true;
        }

        if (syntax.Body is BlockSyntax block)
        {
            var statements = ImmutableArray.CreateBuilder<BoundNode>();
            foreach (var statement in block.Statements)
            {
                statements.Add(BindStatement(statement));
            }

            symbol.BoundBody = new BoundNode(BoundKind.Block, block.Span, UnitTypeSymbol.Instance, null, statements.ToImmutable());
            if (symbol.ReturnType is not UnitTypeSymbol && !AlwaysReturns(block.Statements))
            {
                Error(DiagnosticCodes.MissingReturn, $"'{symbol.Name}' her yolda değer döndürmelidir.", syntax.Name.Span);
            }

            CheckOutputs(syntax.Name.Span);
        }
        else if (syntax.Body is ExpressionSyntax expression)
        {
            var value = BindExpression(expression, false);
            symbol.BoundBody = BoundNode.Create(BoundKind.Result, expression.Span, value.Type, null, false, value);
            if (symbol.ReturnType is UnitTypeSymbol)
            {
                Error(DiagnosticCodes.UnexpectedResult, "Değer döndürmeyen fonksiyonda => yazılamaz.", syntax.Arrow?.Span ?? expression.Span);
            }
            else
            {
                Expect(DiagnosticCodes.TypeMismatch, value.Type, symbol.ReturnType, expression.Span, "Sonuç ifadesi dönüş tipine uymuyor.");
            }

            CheckOutputs(syntax.Name.Span);
        }

        _scope = previousScope;
        _flow = previousFlow;
        _function = previousFunction;
        _currentType = previousType;
        _awaitAllowed = previousAwait;
        _staticContext = previousStatic;
    }

    private void CheckOutputs(TextSpan span)
    {
        if (_function is null)
        {
            return;
        }

        foreach (var parameter in _function.Parameters.Where(parameter => parameter.IsOut))
        {
            if (!_flow.Assigned.Contains(parameter))
            {
                Error(DiagnosticCodes.OutputAssignment, $"'{parameter.Name}' çıktı parametresi atanmalıdır.", span);
            }
        }
    }

    private bool AlwaysReturns(IEnumerable<SyntaxNode> statements)
    {
        foreach (var statement in statements)
        {
            if (AlwaysReturns(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool AlwaysReturns(SyntaxNode node) => node switch
    {
        ResultStatementSyntax => true,
        BlockSyntax block => AlwaysReturns(block.Statements),
        IfStatementSyntax ifStatement when ifStatement.ElseBody is not null => AlwaysReturns(ifStatement.Body) && ifStatement.ElseIfs.All(clause => AlwaysReturns(clause.Body)) && AlwaysReturns(ifStatement.ElseBody),
        TryStatementSyntax tried when tried.Body is not null => AlwaysReturns(tried.Body) && tried.Catches.All(clause => AlwaysReturns(clause.Body)),
        _ => false,
    };

    private BoundNode BindExpression(ExpressionSyntax expression, bool assignmentTarget, TypeSymbol? expected = null)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal:
                return BindLiteral(literal, expected);
            case IdentifierExpressionSyntax identifier:
                return BindName(Text(identifier.Name), identifier.Span, assignmentTarget);
            case CurrentItemExpressionSyntax current:
                if (_currentItem is null)
                {
                    Error(DiagnosticCodes.CurrentItemContext, "^ burada geçerli değil.", current.Span);
                    return ErrorNode(current.Span);
                }

                return BoundNode.Leaf(BoundKind.Name, current.Span, _currentItem, op: TokenKind.Caret);
            case ThisExpressionSyntax:
                if (_currentType is null || _staticContext || _function is null)
                {
                    Error(DiagnosticCodes.ThisContext, "bu yalnız örnek üyesi içinde yazılabilir.", expression.Span);
                    return ErrorNode(expression.Span);
                }

                return BoundNode.Leaf(BoundKind.Name, expression.Span, _currentType, op: TokenKind.Bu);
            case BaseExpressionSyntax:
                if (_currentType?.BaseType is not TypeSymbol)
                {
                    Error(DiagnosticCodes.BaseContext, "temel yalnız türemiş örnek içinde yazılabilir.", expression.Span);
                    return ErrorNode(expression.Span);
                }

                return BoundNode.Leaf(BoundKind.Name, expression.Span, _currentType.BaseType, op: TokenKind.Temel);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression, assignmentTarget);
            case UnaryExpressionSyntax unary:
                return BindUnary(unary, assignmentTarget);
            case BinaryExpressionSyntax binary:
                return BindBinary(binary);
            case AssignmentExpressionSyntax assignment:
                return BindAssignment(assignment);
            case MemberAccessExpressionSyntax member:
                return BindMember(member, assignmentTarget);
            case InvocationExpressionSyntax call:
                return BindInvocation(call, expected);
            case IndexExpressionSyntax index:
                return BindIndex(index);
            case ArrayExpressionSyntax array:
                return BindArray(array, expected);
            case InterpolationExpressionSyntax interpolation:
                return BindInterpolation(interpolation);
            case ObjectCreationExpressionSyntax creation:
                return BindCreation(creation);
            case CopyExpressionSyntax copy:
                return BindCopy(copy);
            case ExpressionIfSyntax conditional:
                return BindExpressionIf(conditional);
            case AwaitExpressionSyntax await:
                if (!_awaitAllowed)
                {
                    Error(DiagnosticCodes.AwaitContext, "bekle yalnız eşzamansız bağlamda yazılabilir.", await.Bekle.Span);
                }

                var awaited = BindExpression(await.Expression, false, expected);
                var awaitType = awaited.Type is UnitTypeSymbol ? UnitTypeSymbol.Instance : awaited.Type;
                return BoundNode.Create(BoundKind.Unary, await.Span, awaitType, null, false, awaited).With(TokenKind.Bekle);
            case LambdaExpressionSyntax lambda:
                return BindLambda(lambda);
            default:
                Error(DiagnosticCodes.UnknownSyntax, "Bu ifade bağlanamadı.", expression.Span);
                return ErrorNode(expression.Span);
        }
    }

    private BoundNode BindLiteral(LiteralExpressionSyntax literal, TypeSymbol? expected)
    {
        if (literal.Token.TokenKind == TokenKind.Tilde)
        {
            if (expected?.IsNullable == true)
            {
                return BoundNode.Leaf(BoundKind.Literal, literal.Span, expected, constant: "~");
            }

            if (expected is not null && expected is not ErrorTypeSymbol)
            {
                Error(DiagnosticCodes.NullNotAllowed, $"'~' '{expected.Name}' tipine atanamaz.", literal.Span);
                return ErrorNode(literal.Span);
            }

            Error(DiagnosticCodes.NullWithoutContext, "~ için nullable hedef tip gerekir.", literal.Span);
            return ErrorNode(literal.Span);
        }

        TypeSymbol type = literal.Token.TokenKind switch
        {
            TokenKind.IntegerLiteral => _tamsayı,
            TokenKind.RealLiteral => _ondalık,
            TokenKind.StringLiteral => _metin,
            TokenKind.CharacterLiteral => _karakter,
            TokenKind.Dogru or TokenKind.Yanlis => _mantıksal,
            TokenKind.Tilde => NullTypeSymbol.Instance,
            _ => ErrorTypeSymbol.Instance,
        };
        if (type is NullTypeSymbol)
        {
            Error(DiagnosticCodes.NullWithoutContext, "~ için nullable hedef tip gerekir.", literal.Span);
            return ErrorNode(literal.Span);
        }

        return BoundNode.Leaf(BoundKind.Literal, literal.Span, type, constant: LiteralConstant(literal));
    }

    private BoundNode BindName(string name, TextSpan span, bool assignmentTarget)
    {
        var found = _scope.Lookup(name);
        if (found.Count == 0 && _currentType is not null && !_staticContext)
        {
            if (FindMember(_currentType, name) is FieldSymbol field)
            {
                if (!CanAccess(field))
                {
                    Error(DiagnosticCodes.NotVisible, $"'{name}' bu konumdan görünmez.", span);
                }

                return BoundNode.Leaf(BoundKind.Name, span, field.Type, field);
            }

            if (FindMember(_currentType, name) is EventSymbol foundEvent)
            {
                if (!CanAccess(foundEvent))
                {
                    Error(DiagnosticCodes.NotVisible, $"'{name}' bu konumdan görünmez.", span);
                }

                return BoundNode.Leaf(BoundKind.Name, span, foundEvent.Type, foundEvent);
            }

            var methods = Methods(_currentType, name);
            if (methods.Count > 0)
            {
                return BoundNode.Leaf(BoundKind.Name, span, new MethodGroupTypeSymbol(methods.ToArray()), methods[0]);
            }
        }

        if (found.Count == 0)
        {
            Error(DiagnosticCodes.UnresolvedName, $"'{name}' tanımlı değil.", span);
            return ErrorNode(span);
        }

        if (found.Count > 1 && found.All(symbol => symbol is FunctionSymbol))
        {
            return BoundNode.Leaf(BoundKind.Name, span, new MethodGroupTypeSymbol(found.Cast<FunctionSymbol>().ToArray()));
        }

        var symbol = found[0];
        if (symbol is VariableSymbol or ParameterSymbol)
        {
            var parameter = symbol as ParameterSymbol;
            var needsValue = parameter is null || !parameter.IsOut ? !assignmentTarget : false;
            if (parameter?.IsOut == true && !assignmentTarget && !_flow.Assigned.Contains(symbol))
            {
                needsValue = true;
            }

            if (symbol is VariableSymbol && !assignmentTarget && !_flow.Assigned.Contains(symbol))
            {
                Error(DiagnosticCodes.DefiniteAssignment, $"'{name}' okunmadan önce atanmalıdır.", span);
            }
            else if (needsValue && parameter?.IsOut == true)
            {
                Error(DiagnosticCodes.DefiniteAssignment, $"'{name}' okunmadan önce atanmalıdır.", span);
            }

            var type = symbol is VariableSymbol variable ? variable.Type : parameter!.Type;
            return BoundNode.Leaf(BoundKind.Name, span, type, symbol);
        }

        if (symbol is TypeSymbol typeSymbol)
        {
            return BoundNode.Leaf(BoundKind.Name, span, typeSymbol, symbol);
        }

        if (symbol is FunctionSymbol function)
        {
            return BoundNode.Leaf(BoundKind.Name, span, new MethodGroupTypeSymbol([function]), function);
        }

        if (symbol is ModuleSymbol)
        {
            return BoundNode.Leaf(BoundKind.Name, span, _nesne, symbol);
        }

        return BoundNode.Leaf(BoundKind.Name, span, ErrorTypeSymbol.Instance, symbol);
    }

    private BoundNode BindUnary(UnaryExpressionSyntax unary, bool assignmentTarget)
    {
        if (unary.IsPostfix || unary.Operator.TokenKind is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            var target = BindExpression(unary.Operand, true);
            if (!IsNumeric(target.Type))
            {
                Error(DiagnosticCodes.UnaryOrAssignment, "++ ve -- sayısal değişkene yazılır.", unary.Span);
            }

            if (target.Symbol is not null)
            {
                _flow = _flow.Add(target.Symbol);
            }

            return BoundNode.Create(BoundKind.Unary, unary.Span, target.Type, target.Symbol, unary.IsPostfix, target).With(unary.Operator.TokenKind);
        }

        var operand = BindExpression(unary.Operand, false);
        if (unary.Operator.TokenKind == TokenKind.Bang)
        {
            if (operand.Type is not ErrorTypeSymbol && !Conversion.Same(operand.Type, _mantıksal))
            {
                Error(DiagnosticCodes.UnaryOrAssignment, "! yalnız mantıksal değere uygulanır.", unary.Span);
            }

            return BoundNode.Create(BoundKind.Unary, unary.Span, _mantıksal, null, false, operand).With(TokenKind.Bang);
        }

        if (!IsNumeric(operand.Type) && operand.Type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.UnaryOrAssignment, "Tekil + ve - sayısal değer ister.", unary.Span);
        }

        return BoundNode.Create(BoundKind.Unary, unary.Span, operand.Type, null, false, operand).With(unary.Operator.TokenKind);
    }

    private BoundNode BindBinary(BinaryExpressionSyntax binary)
    {
        var left = BindExpression(binary.Left, false);
        var right = BindExpression(binary.Right, false);
        var op = binary.Operator.TokenKind;
        if (op is TokenKind.AmpersandAmpersand or TokenKind.PipePipe)
        {
            Expect(DiagnosticCodes.BinaryType, left.Type, _mantıksal, binary.Left.Span, "&& ve || mantıksal ister.");
            Expect(DiagnosticCodes.BinaryType, right.Type, _mantıksal, binary.Right.Span, "&& ve || mantıksal ister.");
            return BoundNode.Create(BoundKind.Binary, binary.Span, _mantıksal, null, false, left, right).With(op);
        }

        if (op is TokenKind.EqualEqual or TokenKind.BangEqual)
        {
            if (Conversion.Common(left.Type, right.Type) is ErrorTypeSymbol && left.Type is not ErrorTypeSymbol && right.Type is not ErrorTypeSymbol)
            {
                Error(DiagnosticCodes.BinaryType, "Bu iki değer karşılaştırılamaz.", binary.Span);
            }

            return BoundNode.Create(BoundKind.Binary, binary.Span, _mantıksal, null, false, left, right).With(op);
        }

        if (op == TokenKind.QuestionQuestion)
        {
            var common = Conversion.Common(left.Type.WithNullable(false), right.Type);
            if (common is ErrorTypeSymbol && left.Type is not ErrorTypeSymbol && right.Type is not ErrorTypeSymbol)
            {
                Error(DiagnosticCodes.BinaryType, "?? kolları ortak tipe dönüşemiyor.", binary.Span);
            }

            return BoundNode.Create(BoundKind.Binary, binary.Span, common, null, false, left, right).With(op);
        }

        if (IsDateOperation(left.Type, right.Type, op, out var dateResult))
        {
            return BoundNode.Create(BoundKind.Binary, binary.Span, dateResult, null, false, left, right).With(op);
        }

        if (op is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual && SameTemporal(left.Type, right.Type))
        {
            return BoundNode.Create(BoundKind.Binary, binary.Span, _mantıksal, null, false, left, right).With(op);
        }

        var result = NumericResult(left.Type, right.Type, binary.Span, op is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual);

        var type = op is TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual ? _mantıksal : result;
        return BoundNode.Create(BoundKind.Binary, binary.Span, type, null, false, left, right).With(op);
    }

    private BoundNode BindAssignment(AssignmentExpressionSyntax assignment)
    {
        var left = BindExpression(assignment.Left, true);
        var right = BindExpression(assignment.Right, false, assignment.Operator.TokenKind == TokenKind.Equals && left.Type is not ErrorTypeSymbol ? left.Type : null);
        if (left.Symbol is EventSymbol ev && assignment.Operator.TokenKind is TokenKind.PlusEquals or TokenKind.MinusEquals)
        {
            if (!Conversion.IsImplicit(right.Type, ev.Type) && right.Type is not MethodGroupTypeSymbol)
            {
                Error(DiagnosticCodes.TypeMismatch, "Olay işleyicisi olay tipine uymuyor.", assignment.Right.Span);
            }
        }
        else if (assignment.Operator.TokenKind != TokenKind.Equals)
        {
            if (!IsNumeric(left.Type) || !IsNumeric(right.Type))
            {
                Error(DiagnosticCodes.UnaryOrAssignment, "Bileşik atama sayısal olmalıdır.", assignment.Span);
            }
        }
        else if (right.Type is NullTypeSymbol && !left.Type.IsNullable)
        {
            Error(DiagnosticCodes.NullNotAllowed, "Bu konuma ~ yazılamaz.", assignment.Right.Span);
        }
        else
        {
            Expect(DiagnosticCodes.IllegalConversion, right.Type, left.Type, assignment.Right.Span, $"'{left.Type.DisplayName}' tipine '{right.Type.DisplayName}' atanamaz.");
        }

        if (left.Symbol is VariableSymbol or ParameterSymbol)
        {
            _flow = _flow.Add(left.Symbol);
        }
        else if (left.Symbol is not EventSymbol && !assignmentTargetIsWritable(left) && left.Type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.UnaryOrAssignment, "Atama hedefi değişken olmalıdır.", assignment.Left.Span);
        }

        return BoundNode.Create(BoundKind.Assignment, assignment.Span, left.Type, left.Symbol, false, left, right).With(assignment.Operator.TokenKind);
    }

    private static bool assignmentTargetIsWritable(BoundNode left) =>
        left.Symbol is VariableSymbol or ParameterSymbol or FieldSymbol || left.Kind == BoundKind.Member || left.Flag && left.Operator == TokenKind.OpenBracket;

    private BoundNode BindMember(MemberAccessExpressionSyntax member, bool assignmentTarget)
    {
        var receiver = BindExpression(member.Expression, false);
        var name = Text(member.Name);
        if (receiver.Symbol is ModuleSymbol moduleSymbol)
        {
            var method = moduleSymbol.Methods.FirstOrDefault(item => item.Name == name);
            if (method is null)
            {
                Error(DiagnosticCodes.UnknownMember, $"'{name}' üyesi tanımlı değil.", member.Name.Span);
                return ErrorNode(member.Span);
            }

            return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, new MethodGroupTypeSymbol([method]), method));
        }

        if (receiver.Type is ErrorTypeSymbol)
        {
            return ErrorNode(member.Span);
        }

        if (receiver.Type is NullTypeSymbol)
        {
            return ErrorNode(member.Span);
        }

        if (receiver.Type is DatabaseTypeSymbol databaseType)
        {
            var table = databaseType.Database.Tables.FirstOrDefault(item => item.Name == name);
            if (table is null)
            {
                Error(DiagnosticCodes.UnknownMember, $"'{name}' tablosu yok.", member.Name.Span);
                return ErrorNode(member.Span);
            }

            return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, new TableQueryTypeSymbol(table), table));
        }

        if (IsDurationName(name) && IsNumeric(receiver.Type.WithNullable(false)))
        {
            return BoundNode.Create(BoundKind.Member, member.Span, _süre, null, false, receiver).With(constant: name);
        }

        if (receiver.Type.WithNullable(false) is PrimitiveTypeSymbol primitive && primitive.Canonical == _metin && name == "uzunluk")
        {
            return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, _tamsayı));
        }

        if (receiver.Type.WithNullable(false) is SourceTypeSymbol source && source.Definition.TypeKind == SourceTypeKind.Option)
        {
            var option = source.Definition.OptionMembers.FirstOrDefault(item => item.Name == name);
            if (option is null)
            {
                Error(DiagnosticCodes.UnknownMember, $"'{name}' seçenekte yok.", member.Name.Span);
                return ErrorNode(member.Span);
            }

            return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, source.Definition, option));
        }

        var memberSymbol = FindMember(receiver.Type, name);
        if (memberSymbol is null)
        {
            if (receiver.Type is PrimitiveTypeSymbol dynamicType && dynamicType.Canonical == _dinamik)
            {
                return BoundNode.Create(BoundKind.Member, member.Span, _dinamik, null, false, receiver);
            }

            if (CollectionDefinition(receiver.Type) is not null && IsCollectionMember(name))
            {
                if (!CollectionMemberAllowed(receiver.Type, name, out var denied))
                {
                    Error(DiagnosticCodes.WrongCollectionMember, denied, member.Name.Span);
                    return ErrorNode(member.Span);
                }

                return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, CollectionMemberType(receiver.Type, name), constant: name));
            }

            if (IsQueryName(name))
            {
                Error(DiagnosticCodes.QueryTarget, $"'{name}' koleksiyon ister.", member.Name.Span);
                return ErrorNode(member.Span);
            }

            Error(DiagnosticCodes.UnresolvedName, $"'{name}' üyesi tanımlı değil.", member.Name.Span);
            return ErrorNode(member.Span);
        }

        if (!CanAccess(memberSymbol))
        {
            Error(DiagnosticCodes.NotVisible, $"'{name}' bu konumdan görünmez.", member.Name.Span);
        }

        var memberType = memberSymbol switch
        {
            FieldSymbol field => Close(field.Type, receiver.Type),
            EventSymbol ev => ev.Type,
            FunctionSymbol function => new MethodGroupTypeSymbol([function]),
            _ => ErrorTypeSymbol.Instance,
        };
        return FinishMember(member, receiver, BoundNode.Leaf(BoundKind.Member, member.Span, memberType, memberSymbol));
    }

    private BoundNode FinishMember(MemberAccessExpressionSyntax member, BoundNode receiver, BoundNode bound)
    {
        if (receiver.Type.IsNullable && member.Operator.TokenKind == TokenKind.Dot)
        {
            Error(DiagnosticCodes.NullableAccess, "Nullable değerin üyesi ?. veya ?? ile kullanılmalıdır.", member.Name.Span);
        }

        var type = member.Operator.TokenKind == TokenKind.QuestionDot ? bound.Type.WithNullable(true) : bound.Type;
        return BoundNode.Create(BoundKind.Member, member.Span, type, bound.Symbol, false, receiver).With(member.Operator.TokenKind, bound.Constant);
    }

    private BoundNode BindInvocation(InvocationExpressionSyntax call, TypeSymbol? expected = null)
    {
        if (call.Expression is MemberAccessExpressionSyntax member)
        {
            var receiver = BindExpression(member.Expression, false);
            var name = Text(member.Name);
            if (receiver.Symbol is ModuleSymbol module)
            {
                return BindModule(call, receiver, module, name, expected);
            }

            if (receiver.Type.WithNullable(false) is PrimitiveTypeSymbol textType && textType.Canonical == _metin && _textMethods.TryGetValue(name, out var textMethod))
            {
                return BindResolvedCall(call, [textMethod], receiver);
            }
            if (name == "tetikle")
            {
                var eventAccess = BindExpression(member.Expression, false);
                if (eventAccess.Symbol is EventSymbol ev)
                {
                    var arguments = BindArgumentList(call.Arguments);
                    if (arguments.Length != 1)
                    {
                        Error(DiagnosticCodes.BadCall, "tetikle bir değer ister.", call.Span);
                    }
                    else
                    {
                        Expect(DiagnosticCodes.TypeMismatch, arguments[0].Type, ev.Type, arguments[0].Span, "Olay değeri olay tipine uymuyor.");
                    }

                    var instance = eventAccess.Kind == BoundKind.Member
                        ? eventAccess.Children[0]
                        : BoundNode.Leaf(BoundKind.Name, member.Span, _currentType ?? ev.Type, op: TokenKind.Bu);
                    var children = ImmutableArray.CreateBuilder<BoundNode>();
                    children.Add(instance);
                    children.AddRange(arguments);
                    return new BoundNode(BoundKind.Call, call.Span, UnitTypeSymbol.Instance, ev, children.ToImmutable(), false, null, "tetikle");
                }
            }

            if (receiver.Type is TableQueryTypeSymbol query)
            {
                return BindTableCall(call, receiver, query, name);
            }

            if (receiver.Type is HttpBuilderTypeSymbol or HttpResponseTypeSymbol || receiver.Symbol is ModuleSymbol { Name: "http" })
            {
                return BindHttpCall(call, receiver, name, expected);
            }

            var definition = CollectionDefinition(receiver.Type);
            if (name == "dönüştür")
            {
                return BindConvert(call, receiver);
            }

            if (definition is null && IsQueryName(name))
            {
                Error(DiagnosticCodes.QueryTarget, $"'{name}' koleksiyon ister.", member.Name.Span);
                return BindArguments(call, ErrorTypeSymbol.Instance, ErrorTypeSymbol.Instance, receiver);
            }

            if (definition is not null && IsCollectionMember(name))
            {
                return BindCollectionCall(call, receiver, name);
            }

            if (receiver.Type is PrimitiveTypeSymbol dynamicType && dynamicType.Canonical == _dinamik)
            {
                return BindArguments(call, _dinamik, _dinamik, receiver);
            }

            var methods = Methods(receiver.Type, name);
            if (methods.Count == 0)
            {
                if (receiver.Type is not ErrorTypeSymbol)
                {
                    Error(DiagnosticCodes.UnknownMember, $"'{name}' üyesi tanımlı değil.", member.Name.Span);
                }

                return BindArguments(call, ErrorTypeSymbol.Instance, ErrorTypeSymbol.Instance, receiver);
            }

            if (!CanAccess(methods[0]))
            {
                Error(DiagnosticCodes.NotVisible, $"'{name}' bu konumdan görünmez.", member.Name.Span);
            }

            var resolved = BindResolvedCall(call, methods, receiver);
            return member.Expression is BaseExpressionSyntax ? resolved.With(TokenKind.Temel) : resolved;
        }

        var target = BindExpression(call.Expression, false);
        if (target.Type is MethodGroupTypeSymbol group)
        {
            return BindResolvedCall(call, group.Functions.ToList(), null);
        }

        if (target.Symbol is FunctionSymbol lambda && lambda.IsLambda)
        {
            return BindResolvedCall(call, [lambda], null);
        }

        if (target.Symbol is VariableSymbol variable && variable.Callable is FunctionSymbol callable)
        {
            return BindResolvedCall(call, [callable], null);
        }

        if (target.Type is ErrorTypeSymbol)
        {
            return BindArguments(call, ErrorTypeSymbol.Instance, ErrorTypeSymbol.Instance, target);
        }

        Error(DiagnosticCodes.BadCall, "Çağrı hedefi fonksiyon değil.", call.Expression.Span);
        return ErrorNode(call.Span);
    }

    private BoundNode BindTableCall(InvocationExpressionSyntax call, BoundNode receiver, TableQueryTypeSymbol query, string name)
    {
        var element = query.Element;
        if (name is "filtrele" or "sırala" or "tsırala")
        {
            var previous = _currentItem;
            _currentItem = element;
            var arguments = BindArgumentList(call.Arguments);
            _currentItem = previous;
            if (arguments.Length != 1)
            {
                Error(DiagnosticCodes.BadCall, $"'{name}' bir ifade ister.", call.Span);
            }
            else
            {
                if (name == "filtrele")
                {
                    Expect(DiagnosticCodes.TypeMismatch, arguments[0].Type, _mantıksal, arguments[0].Span, "Süzgeç mantıksal olmalıdır.");
                }

                RejectQueryEffects(arguments[0]);
            }

            return TableCall(call, query, name, receiver, arguments);
        }

        if (name == "birlikte")
        {
            return BindInclude(call, receiver, query);
        }

        if (name == "tümü")
        {
            if (call.Arguments.Arguments.Length != 0)
            {
                Error(DiagnosticCodes.BadCall, "'tümü' argüman almaz.", call.Span);
            }

            return TableCall(call, ListOf(element), name, receiver, []);
        }

        if (name is "ekle" or "güncelle" or "sil")
        {
            var arguments = BindArgumentList(call.Arguments);
            if (arguments.Length != 1)
            {
                Error(DiagnosticCodes.BadCall, $"'{name}' bir kayıt ister.", call.Span);
            }
            else if (arguments[0].Type is not ErrorTypeSymbol && !Conversion.Same(arguments[0].Type.WithNullable(false), element.WithNullable(false)))
            {
                Error(DiagnosticCodes.TypeMismatch, $"'{name}' tablo modelini ister.", arguments[0].Span);
            }

            return TableCall(call, UnitTypeSymbol.Instance, name, receiver, arguments);
        }

        Error(DiagnosticCodes.UnknownMember, $"'{name}' üyesi tanımlı değil.", call.Span);
        return TableCall(call, ErrorTypeSymbol.Instance, name, receiver, []);
    }

    private BoundNode BindInclude(InvocationExpressionSyntax call, BoundNode receiver, TableQueryTypeSymbol query)
    {
        if (call.Arguments.Arguments.Length != 1)
        {
            Error(DiagnosticCodes.BadCall, "'birlikte' bir üye yolu ister.", call.Span);
            return TableCall(call, query, "birlikte", receiver, []);
        }

        var expression = call.Arguments.Arguments[0].Expression;
        if (expression is MemberAccessExpressionSyntax member && member.Expression is MemberAccessExpressionSyntax)
        {
            Error(DiagnosticCodes.NestedInclude, "İç içe ilişki bu sürümde desteklenmiyor.", member.Span);
            return TableCall(call, query, "birlikte", receiver, []);
        }

        if (expression is not MemberAccessExpressionSyntax path || path.Expression is not CurrentItemExpressionSyntax)
        {
            Error(DiagnosticCodes.IncludeMember, "İlişki ^.alan biçiminde yazılmalıdır.", expression.Span);
            return TableCall(call, query, "birlikte", receiver, []);
        }

        var fieldName = Text(path.Name);
        if (FindMember(query.Element, fieldName) is not FieldSymbol field)
        {
            Error(DiagnosticCodes.IncludeMember, $"'{fieldName}' alanı yok.", path.Name.Span);
            return TableCall(call, query, "birlikte", receiver, []);
        }

        if (field.Type.WithNullable(false) is not SourceTypeSymbol related || related.Definition.TypeKind != SourceTypeKind.Model)
        {
            Error(DiagnosticCodes.IncludeMember, $"'{fieldName}' alanı model değil.", path.Name.Span);
            return TableCall(call, query, "birlikte", receiver, []);
        }

        var bound = BoundNode.Leaf(BoundKind.Member, path.Span, field.Type, field);
        return TableCall(call, query, "birlikte", receiver, [bound]);
    }

    private void RejectQueryEffects(BoundNode node)
    {
        if (node.Kind is BoundKind.Call or BoundKind.Assignment || node.Operator == TokenKind.Bekle)
        {
            Error(DiagnosticCodes.QueryEffect, "Veritabanı sorgusunda çağrı veya yan etki yazılamaz.", node.Span);
            return;
        }

        foreach (var child in node.Children)
        {
            RejectQueryEffects(child);
        }
    }

    private BoundNode TableCall(InvocationExpressionSyntax call, TypeSymbol type, string name, BoundNode receiver, ImmutableArray<BoundNode> arguments)
    {
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(receiver);
        children.AddRange(arguments);
        return new BoundNode(BoundKind.Call, call.Span, type, null, children.ToImmutable(), false, null, name);
    }

    private TypeSymbol ListOf(TypeSymbol element) =>
        new ConstructedTypeSymbol((GenericDefinitionSymbol)_root.FindLocal("liste")!, [element], false);

    private BoundNode BindCollectionCall(InvocationExpressionSyntax call, BoundNode receiver, string name)
    {
        if (!CollectionMemberAllowed(receiver.Type, name, out var denied))
        {
            Error(DiagnosticCodes.WrongCollectionMember, denied, call.Span);
        }

        var element = ElementType(receiver.Type) ?? ErrorTypeSymbol.Instance;
        var previous = _currentItem;
        if (name is "filtrele" or "sırala" or "tsırala" or "varMı" or "bul" or "ilk" or "ilkYadaBoş" or "tümü" or "toplam" or "ortalama" or "min" or "maks" or "adet")
        {
            _currentItem = element;
        }

        var arguments = BindArgumentList(call.Arguments);
        _currentItem = previous;
        ValidateCollectionCall(receiver.Type, name, element, arguments, call.Span);
        var result = CollectionMemberType(receiver.Type, name);
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(receiver);
        children.AddRange(arguments);
        return new BoundNode(BoundKind.Call, call.Span, result, null, children.ToImmutable(), false, null, name);
    }

    private BoundNode BindConvert(InvocationExpressionSyntax call, BoundNode receiver)
    {
        if (call.TypeArguments is null || call.TypeArguments.Arguments.Length != 1)
        {
            Error(DiagnosticCodes.MissingMapping, "dönüştür bir hedef tip ister.", call.Span);
            return ErrorNode(call.Span);
        }

        var destination = ResolveType(call.TypeArguments.Arguments[0]);
        var mapping = _mappings.FirstOrDefault(item => Conversion.Same(item.Source, receiver.Type.WithNullable(false)) && Conversion.Same(item.Destination, destination.WithNullable(false)));
        if (mapping is null)
        {
            Error(DiagnosticCodes.MissingMapping, "Bu kaynak ve hedef için dönüşüm yok.", call.Span);
            return ErrorNode(call.Span);
        }

        return new BoundNode(BoundKind.Call, call.Span, destination, mapping, [receiver]);
    }

    private static TypeSymbol LambdaOrSelf(BoundNode node) => node.Type;

    private BoundNode BindResolvedCall(InvocationExpressionSyntax call, IReadOnlyList<FunctionSymbol> functions, BoundNode? receiver)
    {
        var arguments = BindArgumentList(call.Arguments);
        var chosen = ResolveOverload(functions, call, arguments, receiver?.Type);
        var result = chosen is null ? ErrorTypeSymbol.Instance : Close(chosen.ReturnType, receiver?.Type);
        if (chosen is not null && result is UnitTypeSymbol && call.Expression is not null && UsedAsValue(call))
        {
            Error(DiagnosticCodes.UnexpectedResult, $"'{chosen.Name}' değer döndürmez.", call.Span);
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        if (receiver is null && chosen is { DeclaringType: not null, IsStatic: false, IsConstructor: false })
        {
            children.Add(BoundNode.Leaf(BoundKind.Name, call.Span, chosen.DeclaringType, op: TokenKind.Bu));
        }

        if (receiver is not null)
        {
            children.Add(receiver);
        }

        children.AddRange(arguments);
        if (chosen is not null)
        {
            foreach (var argument in arguments)
            {
                if (argument.Operator == TokenKind.Cikti && argument.Symbol is VariableSymbol or ParameterSymbol)
                {
                    _flow = _flow.Add(argument.Symbol);
                }
            }
        }

        return new BoundNode(BoundKind.Call, call.Span, result, chosen, children.ToImmutable());
    }

    private BoundNode BindArguments(InvocationExpressionSyntax call, TypeSymbol argumentType, TypeSymbol result, BoundNode receiver)
    {
        var arguments = BindArgumentList(call.Arguments);
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(receiver);
        children.AddRange(arguments);
        _ = argumentType;
        return new BoundNode(BoundKind.Call, call.Span, result, null, children.ToImmutable());
    }

    private ImmutableArray<BoundNode> BindArgumentList(ArgumentListSyntax list)
    {
        var builder = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var argument in list.Arguments)
        {
            // Spec: ref requires an initialized variable; çıktı may be unassigned at the call site.
            var allowUnassigned = argument.Modifier?.TokenKind == TokenKind.Cikti;
            var value = BindExpression(argument.Expression, allowUnassigned);
            if (argument.Modifier is not null && value.Symbol is not (VariableSymbol or ParameterSymbol or FieldSymbol))
            {
                Error(DiagnosticCodes.RefTarget, "ref ve çıktı değişken ister.", argument.Expression.Span);
            }

            builder.Add(argument.Modifier is null ? value : value.With(argument.Modifier.TokenKind));
        }

        return builder.ToImmutable();
    }

    private FunctionSymbol? ResolveOverload(IReadOnlyList<FunctionSymbol> functions, InvocationExpressionSyntax call, ImmutableArray<BoundNode> arguments, TypeSymbol? owner)
    {
        if (call.Expression is IdentifierExpressionSyntax or MemberAccessExpressionSyntax && functions.Count == 1 && functions[0].TypeParameters.Count > 0 && (functions[0].DeclaringType?.TypeParameters.Count ?? 0) == 0)
        {
            return Infer(functions[0], arguments, call.Span);
        }

        var viable = new List<(FunctionSymbol Function, int Score)>();
        foreach (var function in functions)
        {
            if (!Matches(function, arguments, owner, out var score))
            {
                continue;
            }

            viable.Add((function, score));
        }

        if (viable.Count == 0)
        {
            if (functions.Count > 0)
            {
                Error(DiagnosticCodes.BadCall, $"'{functions[0].Name}' çağrısı argümanlara uymuyor.", call.Span);
            }

            return null;
        }

        var best = viable.Min(item => item.Score);
        var winners = viable.Where(item => item.Score == best).ToArray();
        if (winners.Length > 1)
        {
            Error(DiagnosticCodes.AmbiguousCall, $"'{winners[0].Function.Name}' için birden çok uygun aday var.", call.Span);
            return winners[0].Function;
        }

        return winners[0].Function;
    }

    private FunctionSymbol Infer(FunctionSymbol function, ImmutableArray<BoundNode> arguments, TextSpan span)
    {
        if (function.Parameters.Count != arguments.Length && !function.Parameters.Skip(arguments.Length).All(parameter => parameter.HasDefault))
        {
            Error(DiagnosticCodes.InferenceFailed, $"'{function.Name}' tip argümanı çıkarılamadı.", span);
            return function;
        }

        var map = new Dictionary<TypeParameterSymbol, TypeSymbol>();
        for (var i = 0; i < arguments.Length && i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type is TypeParameterSymbol parameter)
            {
                var key = parameter.Canonical ?? parameter;
                var inferred = arguments[i].Type.WithNullable(false);
                if (map.TryGetValue(key, out var existing) && !Conversion.Same(existing, inferred))
                {
                    Error(DiagnosticCodes.InferenceFailed, $"'{function.Name}' tip argümanı çıkarılamadı.", span);
                    return function;
                }

                map[key] = inferred;
            }
        }

        if (function.TypeParameters.Any(parameter => !map.ContainsKey(parameter)))
        {
            Error(DiagnosticCodes.InferenceFailed, $"'{function.Name}' tip argümanı çıkarılamadı.", span);
            return function;
        }

        foreach (var parameter in function.TypeParameters)
        {
            if (!map.TryGetValue(parameter, out var argument))
            {
                continue;
            }

            foreach (var constraint in parameter.Constraints)
            {
                if (!Conversion.Derives(argument, constraint) && !Conversion.Same(argument, constraint))
                {
                    Error(DiagnosticCodes.ConstraintFailed, $"'{argument.DisplayName}' '{constraint.DisplayName}' kısıtını karşılamıyor.", span);
                }
            }
        }

        var substituted = function.ReturnType is TypeParameterSymbol returned && map.TryGetValue(returned.Canonical ?? returned, out var mapped)
            ? mapped
            : function.ReturnType;
        var inferredFunction = new FunctionSymbol(function.Name)
        {
            ReturnType = substituted,
            Template = function,
            DeclaringType = function.DeclaringType,
            HasBody = function.HasBody,
            IsStatic = function.IsStatic,
        };
        inferredFunction.Parameters.AddRange(function.Parameters);
        return inferredFunction;
    }

    private bool Matches(FunctionSymbol function, ImmutableArray<BoundNode> arguments, TypeSymbol? owner, out int score)
    {
        score = 0;
        var required = function.Parameters.TakeWhile(parameter => !parameter.HasDefault).Count();
        if (arguments.Length < required || arguments.Length > function.Parameters.Count)
        {
            return false;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = function.Parameters[i];
            var argument = arguments[i];
            var hasRef = argument.Operator == TokenKind.Ref;
            var hasOut = argument.Operator == TokenKind.Cikti;
            if (parameter.IsRef != hasRef || parameter.IsOut != hasOut)
            {
                return false;
            }

            if (!Conversion.IsImplicit(argument.Type, Close(parameter.Type, owner)))
            {
                return false;
            }

            score += Conversion.Same(argument.Type, parameter.Type) ? 0 : 1;
        }

        return true;
    }

    private bool UsedAsValue(InvocationExpressionSyntax call) => false;

    private BoundNode BindIndex(IndexExpressionSyntax index)
    {
        var receiver = BindExpression(index.Expression, false);
        var argument = BindExpression(index.Index, false);
        Expect(DiagnosticCodes.BinaryType, argument.Type, _tamsayı, index.Index.Span, "İndeks tamsayı olmalıdır.");
        var element = ElementType(receiver.Type) ?? (Conversion.Same(receiver.Type.WithNullable(false), _metin) ? _karakter : ErrorTypeSymbol.Instance);
        if (element is ErrorTypeSymbol && receiver.Type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.QueryTarget, "Bu değer indislenemez.", index.Span);
        }

        return BoundNode.Create(BoundKind.Other, index.Span, element, null, true, receiver, argument).With(TokenKind.OpenBracket);
    }

    private BoundNode BindArray(ArrayExpressionSyntax array, TypeSymbol? expected)
    {
        var elementExpected = expected is ConstructedTypeSymbol constructed ? constructed.Arguments.FirstOrDefault() : null;
        if (expected is ConstructedTypeSymbol dictionary && CollectionDefinition(dictionary)?.Name == "sözlük" && array.Elements.Length > 0)
        {
            Error(DiagnosticCodes.IllegalConversion, "sözlük literali yoktur. ekle kullanın.", array.Span);
            return ErrorNode(array.Span);
        }

        if (array.Elements.Length == 0)
        {
            if (expected is ConstructedTypeSymbol target && CollectionDefinition(target) is not null)
            {
                if (CollectionDefinition(target)!.Name == "sözlük")
                {
                    return BoundNode.Leaf(BoundKind.Other, array.Span, target, op: TokenKind.OpenBracket);
                }

                return BoundNode.Leaf(BoundKind.Other, array.Span, target, op: TokenKind.OpenBracket);
            }

            Error(DiagnosticCodes.IllegalConversion, "Boş dizi için hedef tip gerekir.", array.Span);
            return ErrorNode(array.Span);
        }

        var elements = array.Elements.Select(element => BindExpression(element, false, elementExpected)).ToArray();
        if (elementExpected is not null)
        {
            foreach (var element in elements)
            {
                Expect(DiagnosticCodes.TypeMismatch, element.Type, elementExpected, element.Span, "Dizi elemanı hedef tipe uymuyor.");
            }

            return new BoundNode(BoundKind.Other, array.Span, expected!, null, [.. elements], false, TokenKind.OpenBracket);
        }

        var type = elements[0].Type;
        foreach (var element in elements.Skip(1))
        {
            var common = Conversion.Common(type, element.Type);
            if (common is ErrorTypeSymbol)
            {
                Error(DiagnosticCodes.TypeMismatch, "Dizi elemanlarının ortak tipi yok.", array.Span);
                return ErrorNode(array.Span);
            }

            type = common;
        }

        var definition = (GenericDefinitionSymbol)_root.FindLocal("liste")!;
        return new BoundNode(BoundKind.Other, array.Span, new ConstructedTypeSymbol(definition, [type], false), null, [.. elements], false, TokenKind.OpenBracket);
    }

    private BoundNode BindInterpolation(InterpolationExpressionSyntax interpolation)
    {
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var piece in interpolation.Pieces)
        {
            if (piece is InterpolationHoleSyntax hole)
            {
                var value = BindExpression(hole.Expression, false);
                if (value.Type is UnitTypeSymbol)
                {
                    Error(DiagnosticCodes.TypeMismatch, "Bu değer metne yazılamaz.", hole.Span);
                }

                children.Add(value);
                continue;
            }

            if (piece is SyntaxToken token)
            {
                var text = InterpolationText(token);
                if (text is not null)
                {
                    children.Add(BoundNode.Leaf(BoundKind.Literal, token.Span, _metin, constant: text));
                }
            }
        }

        return new BoundNode(BoundKind.Other, interpolation.Span, _metin, null, children.ToImmutable(), false, TokenKind.InterpolationStart);
    }

    private BoundNode BindCreation(ObjectCreationExpressionSyntax creation)
    {
        var type = ResolveType(creation.Type);
        var named = DefinitionOf(type);
        if (named?.IsAbstract == true)
        {
            Error(DiagnosticCodes.AbstractCreation, $"'{named.Name}' soyut olduğu için oluşturulamaz.", creation.At.Span);
        }

        var arguments = creation.Arguments is null ? ImmutableArray<BoundNode>.Empty : BindArgumentList(creation.Arguments);
        FunctionSymbol? constructor = null;
        if (creation.Arguments is not null && named is not null)
        {
            var constructors = named.Methods.Where(method => method.IsConstructor).ToList();
            if (constructors.Count == 0 && arguments.Length > 0)
            {
                Error(DiagnosticCodes.BadCall, $"'{named.Name}' bu argümanlarla oluşturulamaz.", creation.Span);
            }
            else if (constructors.Count > 0)
            {
                constructor = ChooseConstructor(constructors, arguments, creation.Span, named.Name);
            }
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.AddRange(arguments);
        foreach (var initializer in creation.Initializers)
        {
            children.Add(BindInitializer(type, initializer));
        }

        return new BoundNode(BoundKind.Call, creation.Span, type, constructor, children.ToImmutable(), false, TokenKind.At);
    }

    private BoundNode BindCopy(CopyExpressionSyntax copy)
    {
        var source = BindExpression(copy.Source, false);
        if (source.Type is SourceTypeSymbol copied && copied.Definition.TypeKind == SourceTypeKind.Class)
        {
            Error(DiagnosticCodes.RefTarget, "ile yalnız model kopyalar.", copy.Span);
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(source);
        foreach (var initializer in copy.Initializers)
        {
            children.Add(BindInitializer(source.Type, initializer));
        }

        return new BoundNode(BoundKind.Other, copy.Span, source.Type, null, children.ToImmutable(), false, TokenKind.Ile);
    }

    private BoundNode BindInitializer(TypeSymbol type, InitializerSyntax initializer)
    {
        var member = FindMember(type, Text(initializer.Name));
        if (member is null && type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.UnknownMember, $"'{Text(initializer.Name)}' üyesi yok.", initializer.Name.Span);
        }

        if (member is FieldSymbol field)
        {
            var target = Close(field.Type, type);
            var value = BindExpression(initializer.Value, false, target);
            Expect(DiagnosticCodes.TypeMismatch, value.Type, target, initializer.Value.Span, "Başlangıç değeri alan tipine uymuyor.");
            return BoundNode.Create(BoundKind.Assignment, initializer.Span, field.Type, field, true, value).With(TokenKind.Equals);
        }

        var unbound = BindExpression(initializer.Value, false);
        return BoundNode.Create(BoundKind.Assignment, initializer.Span, unbound.Type, member, true, unbound).With(TokenKind.Equals);
    }

    private static TypeSymbol Close(TypeSymbol type, TypeSymbol? owner)
    {
        if (owner is not ConstructedTypeSymbol constructed || constructed.Definition is not SourceTypeSymbol source || type is not TypeParameterSymbol parameter)
        {
            return type;
        }

        var index = source.Definition.TypeParameters.FindIndex(item => ReferenceEquals(item, parameter) || ReferenceEquals(item, parameter.Canonical) || item.Name == parameter.Name);
        if (index < 0 || index >= constructed.Arguments.Length)
        {
            return type;
        }

        var argument = constructed.Arguments[index];
        return type.IsNullable ? argument.WithNullable(true) : argument;
    }

    private static SourceTypeSymbol? DefinitionOf(TypeSymbol type) => type switch
    {
        SourceTypeSymbol source => source.Definition,
        ConstructedTypeSymbol constructed when constructed.Definition is SourceTypeSymbol source => source.Definition,
        _ => null,
    };

    private FunctionSymbol? ChooseConstructor(List<FunctionSymbol> constructors, ImmutableArray<BoundNode> arguments, TextSpan span, string name)
    {
        var viable = new List<(FunctionSymbol Function, int Score)>();
        foreach (var constructor in constructors)
        {
            if (Matches(constructor, arguments, null, out var score))
            {
                viable.Add((constructor, score));
            }
        }

        if (viable.Count == 0)
        {
            Error(DiagnosticCodes.BadCall, $"'{name}' bu argümanlarla oluşturulamaz.", span);
            return null;
        }

        var best = viable.Min(item => item.Score);
        var winners = viable.Where(item => item.Score == best).ToArray();
        if (winners.Length > 1)
        {
            Error(DiagnosticCodes.AmbiguousCall, $"'{name}' için birden çok oluştur var.", span);
        }

        var chosen = winners[0].Function;
        if (!CanAccess(chosen))
        {
            Error(DiagnosticCodes.NotVisible, $"'{name}' oluşturucusu bu konumdan görünmez.", span);
        }

        return chosen;
    }

    private void BindConstructorBody(FunctionSymbol symbol, ConstructorDeclarationSyntax syntax, Scope parent)
    {
        var previousScope = _scope;
        var previousFlow = _flow;
        var previousFunction = _function;
        var previousType = _currentType;
        var previousStatic = _staticContext;
        _scope = new Scope(parent);
        _flow = Flow.Empty;
        foreach (var parameter in symbol.Parameters)
        {
            _scope.TryDeclare(parameter);
            if (!parameter.IsOut)
            {
                _flow = _flow.Add(parameter);
            }
        }

        _function = symbol;
        _currentType = symbol.DeclaringType;
        _staticContext = false;
        if (syntax.Body is BlockSyntax block)
        {
            var statements = ImmutableArray.CreateBuilder<BoundNode>();
            foreach (var statement in block.Statements)
            {
                statements.Add(BindStatement(statement));
            }

            symbol.BoundBody = new BoundNode(BoundKind.Block, block.Span, UnitTypeSymbol.Instance, null, statements.ToImmutable());
        }
        else if (syntax.Body is ExpressionSyntax expression)
        {
            symbol.BoundBody = BoundNode.Create(BoundKind.Expression, expression.Span, UnitTypeSymbol.Instance, null, false, BindExpression(expression, false));
        }

        _scope = previousScope;
        _flow = previousFlow;
        _function = previousFunction;
        _currentType = previousType;
        _staticContext = previousStatic;
    }

    private BoundNode BindExpressionIf(ExpressionIfSyntax conditional)
    {
        var condition = BindCondition(conditional.Condition);
        var whenTrue = BindExpression(conditional.WhenTrue, false);
        var whenFalse = BindExpression(conditional.WhenFalse, false);
        var type = Conversion.Common(whenTrue.Type, whenFalse.Type);
        if (type is ErrorTypeSymbol && whenTrue.Type is not ErrorTypeSymbol && whenFalse.Type is not ErrorTypeSymbol)
        {
            Error(DiagnosticCodes.ExpressionIfArms, "eğer kolları ortak tipe dönüşemiyor.", conditional.Span);
        }

        return BoundNode.Create(BoundKind.If, conditional.Span, type, null, false, condition, whenTrue, whenFalse).With(TokenKind.EqualsGreater);
    }

    private BoundNode BindLambda(LambdaExpressionSyntax lambda)
    {
        var previous = _scope;
        var previousFlow = _flow;
        _scope = new Scope(previous);
        _flow = previousFlow;
        var parameters = new List<ParameterSymbol>();
        foreach (var parameter in lambda.Parameters)
        {
            var type = ResolveType(parameter.Type);
            var symbol = new ParameterSymbol(Text(parameter.Name), type, parameter.Modifier?.TokenKind == TokenKind.Ref, parameter.Modifier?.TokenKind == TokenKind.Cikti, parameter.DefaultValue is not null);
            DeclareValue(symbol, parameter.Name.Span);
            _flow = _flow.Add(symbol);
            parameters.Add(symbol);
        }

        BoundNode body;
        TypeSymbol result;
        if (lambda.Body is BlockSyntax block)
        {
            var statements = ImmutableArray.CreateBuilder<BoundNode>();
            foreach (var statement in block.Statements)
            {
                statements.Add(BindStatement(statement));
            }

            body = new BoundNode(BoundKind.Block, block.Span, UnitTypeSymbol.Instance, null, statements.ToImmutable());
            result = statements.FirstOrDefault(statement => statement.Kind == BoundKind.Result)?.Type ?? _mantıksal;
            if (!AlwaysReturns(block.Statements))
            {
                Error(DiagnosticCodes.MissingReturn, "Lambda her yolda değer döndürmelidir.", lambda.Span);
            }
        }
        else if (lambda.Body is ExpressionSyntax expression)
        {
            var value = BindExpression(expression, false);
            body = BoundNode.Create(BoundKind.Result, expression.Span, value.Type, null, false, value);
            result = value.Type;
        }
        else
        {
            body = BoundNode.Leaf(BoundKind.Error, lambda.Span, ErrorTypeSymbol.Instance);
            result = ErrorTypeSymbol.Instance;
        }

        var function = new FunctionSymbol("lambda")
        {
            IsLambda = true,
            HasBody = true,
            BoundBody = body,
            ReturnType = result,
        };
        function.Parameters.AddRange(parameters);
        _scope = previous;
        _flow = previousFlow;
        return BoundNode.Leaf(BoundKind.Other, lambda.Span, result, function, op: TokenKind.EqualsGreater);
    }

    private FunctionSymbol CreateFunction(FunctionDeclarationSyntax syntax, SourceTypeSymbol? type, Visibility visibility)
    {
        var function = new FunctionSymbol(Text(syntax.Name))
        {
            IsAsync = syntax.Keyword.TokenKind == TokenKind.Afonksiyon,
            DeclaringType = type,
            Visibility = visibility,
            HasBody = syntax.Body is not null,
            IsStatic = syntax.Modifiers.Any(token => token.TokenKind == TokenKind.Statik),
        };
        if (syntax.TypeParameters is not null)
        {
            foreach (var parameter in syntax.TypeParameters.Parameters)
            {
                var typeParameter = new TypeParameterSymbol(Text(parameter.Name));
                foreach (var constraint in parameter.Constraints)
                {
                    var resolved = ResolveType(constraint);
                    typeParameter.Constraints.Add(resolved);
                }

                function.TypeParameters.Add(typeParameter);
            }
        }

        var previous = _scope;
        if (function.TypeParameters.Count > 0)
        {
            _scope = new Scope(previous);
            foreach (var parameter in function.TypeParameters)
            {
                _scope.TryDeclare(parameter);
            }
        }

        AddParameters(function, syntax.Parameters);
        function.ReturnType = syntax.ReturnType is null ? UnitTypeSymbol.Instance : ResolveType(syntax.ReturnType);
        _scope = previous;
        return function;
    }

    private void AddParameters(FunctionSymbol function, ImmutableArray<ParameterSyntax> parameters)
    {
        var seenOptional = false;
        foreach (var parameter in parameters)
        {
            var type = ResolveType(parameter.Type);
            var hasDefault = parameter.DefaultValue is not null;
            BoundNode? defaultValue = null;
            if (hasDefault)
            {
                seenOptional = true;
                defaultValue = BindExpression(parameter.DefaultValue!, false);
                Expect(DiagnosticCodes.TypeMismatch, defaultValue.Type, type, parameter.DefaultValue!.Span, "Varsayılan değer parametre tipine uymuyor.");
            }
            else if (seenOptional)
            {
                Error(DiagnosticCodes.OptionalOrder, "Varsayılansız parametre varsayılanlı parametreden sonra gelemez.", parameter.Name.Span);
            }

            function.Parameters.Add(new ParameterSymbol(Text(parameter.Name), type, parameter.Modifier?.TokenKind == TokenKind.Ref, parameter.Modifier?.TokenKind == TokenKind.Cikti, hasDefault) { Containing = function, Default = defaultValue });
        }
    }

    private TypeSymbol ResolveType(TypeSyntax syntax)
    {
        var name = Text(syntax.Name);
        TypeSymbol? resolved = _scope.Lookup(name).OfType<TypeSymbol>().FirstOrDefault();
        if (resolved is null)
        {
            Error(DiagnosticCodes.UnresolvedName, $"'{name}' tipi tanımlı değil.", syntax.Name.Span);
            return ErrorTypeSymbol.Instance;
        }

        if (syntax.Arguments is not null)
        {
            if (resolved is GenericDefinitionSymbol generic)
            {
                if (generic.Arity != syntax.Arguments.Arguments.Length)
                {
                    Error(DiagnosticCodes.GenericArity, $"'{name}' {generic.Arity} tip argümanı ister.", syntax.Span);
                    return ErrorTypeSymbol.Instance;
                }
            }
            else if (resolved is SourceTypeSymbol source && source.Definition.TypeParameters.Count != syntax.Arguments.Arguments.Length)
            {
                Error(DiagnosticCodes.GenericArity, $"'{name}' tip argüman sayısı uymuyor.", syntax.Span);
                return ErrorTypeSymbol.Instance;
            }

            var arguments = syntax.Arguments.Arguments.Select(ResolveType).ToImmutableArray();
            if (resolved is SourceTypeSymbol named)
            {
                for (var i = 0; i < named.Definition.TypeParameters.Count && i < arguments.Length; i++)
                {
                    foreach (var constraint in named.Definition.TypeParameters[i].Constraints)
                    {
                        if (!Conversion.Derives(arguments[i], constraint) && !Conversion.Same(arguments[i], constraint.WithNullable(false)))
                        {
                            Error(DiagnosticCodes.ConstraintFailed, $"'{arguments[i].DisplayName}' '{constraint.DisplayName}' kısıtını karşılamıyor.", syntax.Arguments.Arguments[i].Span);
                        }
                    }
                }
            }

            resolved = new ConstructedTypeSymbol(resolved is SourceTypeSymbol sourceType ? sourceType.Definition : resolved, arguments, false);
        }

        if (syntax.Nullables.Length > 0)
        {
            resolved = resolved.WithNullable(true);
        }

        return resolved;
    }

    private bool Declare(Symbol symbol, TextSpan span)
    {
        if (symbol is FunctionSymbol && _scope.FindLocalAll(symbol.Name).OfType<FunctionSymbol>().Any(item => item.Intrinsic != Intrinsic.None))
        {
            Error(DiagnosticCodes.DuplicateName, $"'{symbol.Name}' standart kapsamdadır.", span);
            return false;
        }

        if (_scope.TryDeclare(symbol))
        {
            return true;
        }

        Error(DiagnosticCodes.DuplicateName, $"'{symbol.Name}' bu kapsamda zaten tanımlı.", span);
        return false;
    }

    private void DeclareValue(Symbol symbol, TextSpan span)
    {
        if (_scope.FindLocal(symbol.Name) is not null)
        {
            Error(DiagnosticCodes.DuplicateName, $"'{symbol.Name}' bu kapsamda zaten tanımlı.", span);
            return;
        }

        if (_scope.Parent?.Lookup(symbol.Name).Any(existing => existing is VariableSymbol or ParameterSymbol or FunctionSymbol or TypeSymbol or ModuleSymbol) == true)
        {
            Error(DiagnosticCodes.Shadowing, $"'{symbol.Name}' dış kapsamdaki adı gölgeliyor.", span);
        }

        _scope.TryDeclare(symbol);
    }

    private Symbol? FindMember(TypeSymbol type, string name)
    {
        if (type is ConstructedTypeSymbol constructed)
        {
            return FindMember(constructed.Definition, name);
        }

        if (type is SourceTypeSymbol source)
        {
            source = source.Definition;
            var field = source.Fields.FirstOrDefault(item => item.Name == name);
            if (field is not null)
            {
                return field;
            }

            var method = source.Methods.FirstOrDefault(item => item.Name == name);
            if (method is not null)
            {
                return method;
            }

            var ev = source.Events.FirstOrDefault(item => item.Name == name);
            if (ev is not null)
            {
                return ev;
            }

            return source.BaseType is null ? null : FindMember(source.BaseType, name);
        }

        return null;
    }

    private List<FunctionSymbol> Methods(TypeSymbol type, string name)
    {
        var list = new List<FunctionSymbol>();
        var seen = new HashSet<SourceTypeSymbol>();
        var current = type;
        while (current is not null)
        {
            var source = current as SourceTypeSymbol ?? (current is ConstructedTypeSymbol constructed ? constructed.Definition as SourceTypeSymbol : null);
            if (source is null || !seen.Add(source.Definition))
            {
                break;
            }

            source = source.Definition;
            list.AddRange(source.Methods.Where(method => method.Name == name && !method.IsConstructor));
            foreach (var face in source.Interfaces.OfType<SourceTypeSymbol>())
            {
                foreach (var method in face.Definition.Methods.Where(method => method.Name == name))
                {
                    if (!list.Any(existing => SameSignature(existing, method)))
                    {
                        list.Add(method);
                    }
                }
            }

            current = source.BaseType;
        }

        var hidden = list.SelectMany(method => method.Targets).ToHashSet();
        list.RemoveAll(hidden.Contains);
        return list;
    }

    private bool CanAccess(Symbol symbol)
    {
        var visibility = symbol switch
        {
            FieldSymbol field => field.Visibility,
            FunctionSymbol function => function.Visibility,
            EventSymbol ev => ev.Visibility,
            _ => Visibility.Public,
        };
        var declaring = symbol switch
        {
            FieldSymbol field => field.DeclaringType,
            FunctionSymbol function => function.DeclaringType,
            EventSymbol ev => ev.DeclaringType,
            _ => null,
        };
        return visibility switch
        {
            Visibility.Public or Visibility.Internal => true,
            Visibility.Private => ReferenceEquals(_currentType?.Definition, declaring?.Definition),
            Visibility.Protected => _currentType is not null && declaring is not null && (ReferenceEquals(_currentType.Definition, declaring.Definition) || Conversion.Derives(_currentType, declaring)),
            _ => false,
        };
    }

    private TypeSymbol? ElementType(TypeSymbol type)
    {
        if (type is ConstructedTypeSymbol constructed && constructed.Definition is GenericDefinitionSymbol { Arity: 1 } && constructed.Arguments.Length == 1)
        {
            return constructed.Arguments[0];
        }

        return null;
    }

    private GenericDefinitionSymbol? CollectionDefinition(TypeSymbol type) =>
        type is ConstructedTypeSymbol constructed ? constructed.Definition as GenericDefinitionSymbol : null;

    private static bool IsQueryName(string name) => name is
        "filtrele" or "sırala" or "tsırala" or "ilk" or "ilkYadaBoş" or "varMı" or "tümü" or "toplam" or "ortalama" or
        "min" or "maks" or "benzersiz" or "atla" or "sınırla" or "dönüştür";

    private bool CollectionMemberAllowed(TypeSymbol receiver, string name, out string message)
    {
        var kind = CollectionDefinition(receiver)?.Name;
        message = "";
        if (kind == "dizi" && name == "adet")
        {
            message = "dizi uzunluk kullanır.";
            return false;
        }

        if (kind is not null && kind != "dizi" && name == "uzunluk")
        {
            message = "uzunluk dizi içindir.";
            return false;
        }

        return true;
    }

    private void ValidateCollectionCall(TypeSymbol receiver, string name, TypeSymbol element, ImmutableArray<BoundNode> arguments, TextSpan span)
    {
        if (name is "filtrele" or "varMı" or "tümü" or "bul")
        {
            RequirePredicate(name, arguments, span, required: name != "ilk");
        }

        if (name is "ilk" or "ilkYadaBoş" && arguments.Length > 1)
        {
            Error(DiagnosticCodes.BadCall, $"{name} en çok bir yüklem ister.", span);
        }

        if (name is "ilk" or "ilkYadaBoş" && arguments.Length == 1)
        {
            RequirePredicate(name, arguments, span, required: true);
        }

        if (name is "sırala" or "tsırala")
        {
            var key = arguments.Length == 0 ? element : arguments[0].Type;
            if (key is not ErrorTypeSymbol && !IsOrderable(key))
            {
                Error(DiagnosticCodes.QuerySelector, $"{name} sıralanabilir anahtar ister.", span);
            }
        }

        if (name is "toplam" or "ortalama" or "min" or "maks")
        {
            var key = arguments.Length == 0 ? element : arguments[0].Type;
            var numeric = name is "toplam" or "ortalama";
            if (key is not ErrorTypeSymbol && (numeric ? !IsNumeric(key) : !IsOrderable(key)))
            {
                Error(DiagnosticCodes.QuerySelector, $"{name} bu anahtarla kullanılamaz.", span);
            }
        }

        if (name is "atla" or "sınırla")
        {
            if (arguments.Length != 1 || arguments[0].Type is not ErrorTypeSymbol && !Conversion.IsImplicit(arguments[0].Type, _tamsayı))
            {
                Error(DiagnosticCodes.BadCall, $"{name} tamsayı ister.", span);
            }
            else if (NegativeInteger(arguments[0]))
            {
                Error(DiagnosticCodes.NegativeWindow, $"{name} negatif olamaz.", arguments[0].Span);
            }
        }

        if (name == "ekle")
        {
            var definition = CollectionDefinition(receiver);
            if (definition?.Name == "sözlük")
            {
                if (arguments.Length != 2)
                {
                    Error(DiagnosticCodes.BadCall, "sözlük ekle anahtar ve değer ister.", span);
                }
                else if (receiver is ConstructedTypeSymbol constructed)
                {
                    Expect(DiagnosticCodes.TypeMismatch, arguments[0].Type, constructed.Arguments[0], arguments[0].Span, "Anahtar tipine uymuyor.");
                    Expect(DiagnosticCodes.TypeMismatch, arguments[1].Type, constructed.Arguments[1], arguments[1].Span, "Değer tipine uymuyor.");
                }
            }
            else if (arguments.Length != 1)
            {
                Error(DiagnosticCodes.BadCall, "ekle bir değer ister.", span);
            }
            else
            {
                Expect(DiagnosticCodes.TypeMismatch, arguments[0].Type, element, arguments[0].Span, "Öğe hedef tipe uymuyor.");
            }
        }

        if (name is "kaldır" or "içerir" && arguments.Length != 1)
        {
            Error(DiagnosticCodes.BadCall, $"{name} bir değer ister.", span);
        }
    }

    private void RequirePredicate(string name, ImmutableArray<BoundNode> arguments, TextSpan span, bool required)
    {
        if (arguments.Length != 1 || arguments[0].Type is not ErrorTypeSymbol && !Conversion.Same(arguments[0].Type.WithNullable(false), _mantıksal))
        {
            if (required || arguments.Length > 0)
            {
                Error(DiagnosticCodes.PredicateType, $"{name} mantıksal sonuç ister.", span);
            }
        }
    }

    private bool IsOrderable(TypeSymbol type)
    {
        type = type.WithNullable(false);
        return IsNumeric(type) || Conversion.Same(type, _metin) || Conversion.Same(type, _karakter);
    }

    private static bool NegativeInteger(BoundNode node)
    {
        if (node.Kind == BoundKind.Literal)
        {
            return int.TryParse(node.Constant, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var count) && count < 0;
        }

        return node.Kind == BoundKind.Unary
            && node.Operator == TokenKind.Minus
            && node.Children.Length == 1
            && node.Children[0].Kind == BoundKind.Literal
            && int.TryParse(node.Children[0].Constant, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var magnitude)
            && magnitude > 0;
    }

    private static bool IsCollectionMember(string name) => name is
        "ekle" or "kaldır" or "içerir" or "bul" or "sırala" or "tersÇevir" or "filtrele" or "tsırala" or
        "ilk" or "ilkYadaBoş" or "varMı" or "tümü" or "adet" or "uzunluk" or "toplam" or "ortalama" or "min" or "maks" or
        "benzersiz" or "atla" or "sınırla";

    private TypeSymbol CollectionMemberType(TypeSymbol receiver, string name)
    {
        var element = ElementType(receiver) ?? ErrorTypeSymbol.Instance;
        return name switch
        {
            "içerir" or "varMı" or "tümü" => _mantıksal,
            "adet" or "uzunluk" => _tamsayı,
            "ilk" or "min" or "maks" => element,
            "ilkYadaBoş" or "bul" => element.WithNullable(true),
            "toplam" or "ortalama" => _ondalık,
            "ekle" or "kaldır" or "tersÇevir" => name == "kaldır" ? _mantıksal : receiver,
            _ => receiver,
        };
    }

    private TypeSymbol NumericResult(TypeSymbol left, TypeSymbol right, TextSpan span, bool comparison)
    {
        if (left is ErrorTypeSymbol || right is ErrorTypeSymbol)
        {
            return ErrorTypeSymbol.Instance;
        }

        if (!IsNumeric(left) || !IsNumeric(right))
        {
            if (comparison && Conversion.Same(left, _metin) && Conversion.Same(right, _metin))
            {
                return _mantıksal;
            }

            Error(DiagnosticCodes.BinaryType, "İşleç bu tiplerle kullanılamaz.", span);
            return ErrorTypeSymbol.Instance;
        }

        if (Conversion.IsImplicit(left, right))
        {
            return right.WithNullable(false);
        }

        if (Conversion.IsImplicit(right, left))
        {
            return left.WithNullable(false);
        }

        Error(DiagnosticCodes.BinaryType, "Sayısal tipler örtük dönüşmüyor.", span);
        return ErrorTypeSymbol.Instance;
    }

    private bool SameTemporal(TypeSymbol left, TypeSymbol right)
    {
        left = left.WithNullable(false);
        right = right.WithNullable(false);
        return Conversion.Same(left, right) && left is PrimitiveTypeSymbol primitive && primitive.Canonical.Name is "tarih" or "saat" or "tarihsaat" or "süre";
    }

    private BoundNode BindModule(InvocationExpressionSyntax call, BoundNode receiver, ModuleSymbol module, string name, TypeSymbol? expected)
    {
        if (module.Name == "http")
        {
            return BindHttpCall(call, receiver, name, expected);
        }

        var method = module.Methods.FirstOrDefault(item => item.Name == name);
        if (method is null)
        {
            Error(DiagnosticCodes.UnknownMember, $"'{name}' üyesi tanımlı değil.", call.Span);
            return ErrorNode(call.Span);
        }

        if (method.Intrinsic == Intrinsic.JsonRead)
        {
            return BindJsonRead(call, receiver, method, expected);
        }

        if (method.Intrinsic == Intrinsic.JsonWrite)
        {
            var arguments = BindArgumentList(call.Arguments);
            if (arguments.Length != 1)
            {
                Error(DiagnosticCodes.BadCall, "json.yaz bir değer ister.", call.Span);
            }

            var children = ImmutableArray.CreateBuilder<BoundNode>();
            children.Add(receiver);
            children.AddRange(arguments);
            return new BoundNode(BoundKind.Call, call.Span, _metin, method, children.ToImmutable());
        }

        return BindResolvedCall(call, [method], receiver);
    }

    private BoundNode BindHttpCall(InvocationExpressionSyntax call, BoundNode receiver, string name, TypeSymbol? expected)
    {
        if (name == "json")
        {
            TypeSymbol result;
            if (call.TypeArguments is { Arguments.Length: 1 })
            {
                result = ResolveType(call.TypeArguments.Arguments[0]);
            }
            else if (expected is not null && expected is not ErrorTypeSymbol && expected is not UnitTypeSymbol)
            {
                result = expected;
            }
            else
            {
                Error(DiagnosticCodes.JsonTarget, "http.json hedef tipi ister.", call.Span);
                result = ErrorTypeSymbol.Instance;
            }

            if (call.Arguments.Arguments.Length != 0)
            {
                Error(DiagnosticCodes.BadCall, "json argüman almaz.", call.Span);
            }

            var json = Procedure("json", Intrinsic.HttpJson, result);
            return new BoundNode(BoundKind.Call, call.Span, result, json, ImmutableArray.Create(receiver));
        }

        var module = _root.FindLocal("http") as ModuleSymbol;
        var method = module?.Methods.FirstOrDefault(item => item.Name == name);
        if (method is null)
        {
            Error(DiagnosticCodes.UnknownMember, $"'{name}' üyesi tanımlı değil.", call.Span);
            return ErrorNode(call.Span);
        }

        if (name == "başlık")
        {
            var headerArgs = BindArgumentList(call.Arguments);
            if (headerArgs.Length != 2)
            {
                Error(DiagnosticCodes.BadCall, "başlık ad ve değer ister.", call.Span);
            }
            else
            {
                Expect(DiagnosticCodes.TypeMismatch, headerArgs[0].Type, _metin, headerArgs[0].Span, "Başlık adı metin olmalıdır.");
                Expect(DiagnosticCodes.TypeMismatch, headerArgs[1].Type, _metin, headerArgs[1].Span, "Başlık değeri metin olmalıdır.");
            }

            var headerChildren = ImmutableArray.CreateBuilder<BoundNode>();
            headerChildren.Add(receiver);
            headerChildren.AddRange(headerArgs);
            return new BoundNode(BoundKind.Call, call.Span, _httpBuilderType!, method, headerChildren.ToImmutable());
        }

        return BindResolvedCall(call, [method], receiver);
    }

    private BoundNode BindJsonRead(InvocationExpressionSyntax call, BoundNode receiver, FunctionSymbol method, TypeSymbol? expected)
    {
        TypeSymbol result;
        if (call.TypeArguments is { Arguments.Length: 1 })
        {
            result = ResolveType(call.TypeArguments.Arguments[0]);
        }
        else if (expected is not null && expected is not ErrorTypeSymbol && expected is not UnitTypeSymbol)
        {
            result = expected;
        }
        else
        {
            Error(DiagnosticCodes.JsonTarget, "json.oku hedef tipi ister.", call.Span);
            result = ErrorTypeSymbol.Instance;
        }

        var arguments = BindArgumentList(call.Arguments);
        if (arguments.Length != 1)
        {
            Error(DiagnosticCodes.BadCall, "json.oku metin ister.", call.Span);
        }
        else
        {
            Expect(DiagnosticCodes.TypeMismatch, arguments[0].Type, _metin, arguments[0].Span, "json.oku metin ister.");
        }

        var children = ImmutableArray.CreateBuilder<BoundNode>();
        children.Add(receiver);
        children.AddRange(arguments);
        return new BoundNode(BoundKind.Call, call.Span, result, method, children.ToImmutable());
    }

    private bool IsDateOperation(TypeSymbol left, TypeSymbol right, TokenKind op, out TypeSymbol result)
    {
        left = left.WithNullable(false);
        right = right.WithNullable(false);
        if (op is TokenKind.Plus or TokenKind.Minus && Conversion.Same(left, _tarihsaat) && Conversion.Same(right, _süre))
        {
            result = _tarihsaat;
            return true;
        }

        if (op == TokenKind.Minus && Conversion.Same(left, _tarihsaat) && Conversion.Same(right, _tarihsaat))
        {
            result = _süre;
            return true;
        }

        if (op is TokenKind.Plus or TokenKind.Minus && Conversion.Same(left, _süre) && Conversion.Same(right, _süre))
        {
            result = _süre;
            return true;
        }

        result = ErrorTypeSymbol.Instance;
        return false;
    }

    private bool IsNumeric(TypeSymbol type)
    {
        type = type.WithNullable(false);
        return type is PrimitiveTypeSymbol primitive && primitive.Canonical.Name is "tamsayı" or "utamsayı" or "ondalık" or "gondalık" or "hondalık";
    }

    private static bool IsDurationName(string name) => name is "milisaniye" or "saniye" or "dakika" or "saat" or "gün" or "hafta";

    private void Expect(string code, TypeSymbol from, TypeSymbol to, TextSpan span, string message)
    {
        if (from is ErrorTypeSymbol || to is ErrorTypeSymbol || Conversion.IsImplicit(from, to))
        {
            return;
        }

        Error(code, message, span);
    }

    private static Flow Merge(Flow left, Flow right) => new(left.Assigned.Intersect(right.Assigned));

    private static Visibility VisibilityOf(ImmutableArray<SyntaxToken> modifiers, bool defaultPrivate)
    {
        if (modifiers.Any(token => token.TokenKind == TokenKind.Genel))
        {
            return Visibility.Public;
        }

        if (modifiers.Any(token => token.TokenKind == TokenKind.Ozel))
        {
            return Visibility.Private;
        }

        if (modifiers.Any(token => token.TokenKind == TokenKind.Korumali))
        {
            return Visibility.Protected;
        }

        if (modifiers.Any(token => token.TokenKind == TokenKind.Dahili))
        {
            return Visibility.Internal;
        }

        return defaultPrivate ? Visibility.Private : Visibility.Public;
    }

    private string Suggestion(ExpressionSyntax expression)
    {
        var text = TextOf(expression);
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1];
        }

        return text;
    }

    private string Text(SyntaxToken token) => TextOf(token.Span);

    private string TextOf(SyntaxNode node) => TextOf(node.Span);

    private string TextOf(TextSpan span) => span.Length == 0 ? string.Empty : _file.Text.ToString(span);

    private string LiteralConstant(LiteralExpressionSyntax literal)
    {
        var raw = TextOf(literal.Span);
        return literal.Token.TokenKind switch
        {
            TokenKind.StringLiteral => DecodeString(raw),
            TokenKind.CharacterLiteral => DecodeCharacter(raw),
            TokenKind.Dogru => "doğru",
            TokenKind.Yanlis => "yanlış",
            _ => raw,
        };
    }

    private string? InterpolationText(SyntaxToken token)
    {
        var raw = TextOf(token.Span);
        return token.TokenKind switch
        {
            TokenKind.StringText => DecodeInterpolated(raw),
            TokenKind.InterpolationMiddle => DecodeInterpolated(TrimHoleBraces(raw, trailingBrace: true)),
            TokenKind.InterpolationEnd when raw.Length > 0 && raw[^1] == '"' => DecodeInterpolated(TrimHoleBraces(raw[..^1], trailingBrace: false)),
            TokenKind.InterpolationStart when raw.Length > 1 && raw[0] == '"' => DecodeInterpolated(raw[1..]),
            _ => null,
        };
    }

    private static string TrimHoleBraces(string text, bool trailingBrace)
    {
        if (text.StartsWith('}'))
        {
            text = text[1..];
        }

        if (trailingBrace && text.EndsWith('{'))
        {
            text = text[..^1];
        }

        return text;
    }

    private static string DecodeInterpolated(string text) =>
        DecodeEscapes(text.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal));

    private static string DecodeString(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            raw = raw[1..^1];
        }

        return DecodeEscapes(raw.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal));
    }

    private static string DecodeCharacter(string raw)
    {
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
        {
            raw = raw[1..^1];
        }

        var text = DecodeEscapes(raw);
        return text.Length == 0 ? "\0" : text;
    }

    private static string DecodeEscapes(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal))
        {
            return raw;
        }

        var builder = new System.Text.StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length)
            {
                builder.Append(raw[i]);
                continue;
            }

            builder.Append(raw[++i] switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '"' => '"',
                '\'' => '\'',
                '\\' => '\\',
                var other => other,
            });
        }

        return builder.ToString();
    }

    private BoundNode ErrorNode(TextSpan span) => BoundNode.Leaf(BoundKind.Error, span, ErrorTypeSymbol.Instance);

    private void Error(string code, string message, TextSpan span, string? suggestion = null)
    {
        var (line, column) = _file.Lines.GetLineAndColumn(Math.Min(span.Start, _file.Text.Length));
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, _file.Path, span, line, column, suggestion));
    }
}

internal readonly record struct Flow(ImmutableHashSet<Symbol> Assigned)
{
    public static Flow Empty { get; } = new(ImmutableHashSet<Symbol>.Empty);

    public Flow Add(Symbol symbol) => new(Assigned.Add(symbol));
}
