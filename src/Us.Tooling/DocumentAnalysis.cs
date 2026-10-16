using System.Collections.Immutable;
using Us.Core;
using Us.Lexer;
using Us.Parser;
using Us.Semantics;

namespace Us.Tooling;

public sealed class DocumentAnalysis
{
    private DocumentAnalysis(
        SourceFile file,
        ParseResult parse,
        BoundCompilation bound,
        SourcePositionMapper positions,
        ImmutableDictionary<Symbol, TextSpan> declarations,
        ImmutableArray<(Symbol Symbol, TextSpan Span, bool IsDeclaration)> references)
    {
        File = file;
        Parse = parse;
        Bound = bound;
        Positions = positions;
        Declarations = declarations;
        References = references;
    }

    public SourceFile File { get; }

    public ParseResult Parse { get; }

    public CompilationUnitSyntax Root => Parse.Root;

    public BoundCompilation Bound { get; }

    public SourcePositionMapper Positions { get; }

    public ImmutableDictionary<Symbol, TextSpan> Declarations { get; }

    public ImmutableArray<(Symbol Symbol, TextSpan Span, bool IsDeclaration)> References { get; }

    public static DocumentAnalysis Analyze(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var file = SourceFile.FromText(path, text);
        var parse = global::Us.Parser.Parser.Parse(file);
        BoundCompilation bound;
        try
        {
            bound = Binder.Bind(file, parse.Root);
        }
        catch
        {
            bound = new BoundCompilation(
                ImmutableArray<BoundNode>.Empty,
                parse.Diagnostics,
                ImmutableArray<TestSymbol>.Empty,
                ImmutableArray<ServiceRegistration>.Empty,
                ImmutableArray<DatabaseSymbol>.Empty,
                ImmutableArray<ControllerSymbol>.Empty,
                ImmutableArray<RouteSymbol>.Empty);
        }

        var declarations = ImmutableDictionary.CreateBuilder<Symbol, TextSpan>(ReferenceEqualityComparer.Instance);
        var references = ImmutableArray.CreateBuilder<(Symbol, TextSpan, bool)>();
        IndexSyntaxDeclarations(file, parse.Root, bound, declarations);
        IndexBound(bound.Members, declarations, references);
        foreach (var function in bound.Functions)
        {
            if (function.BoundBody is not null)
            {
                IndexBound([function.BoundBody], declarations, references);
            }
        }

        return new DocumentAnalysis(
            file,
            parse,
            bound,
            new SourcePositionMapper(file.Text),
            declarations.ToImmutable(),
            references.ToImmutable());
    }

    public IReadOnlyList<ToolingDiagnostic> GetDiagnostics()
    {
        var bag = new List<Diagnostic>();
        bag.AddRange(Parse.Diagnostics.ToImmutableArray());
        bag.AddRange(Bound.Diagnostics.ToImmutableArray());

        return bag
            .Select(diagnostic => new ToolingDiagnostic(
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                Positions.ToRange(diagnostic.Span),
                diagnostic.Suggestion ?? string.Empty))
            .ToArray();
    }

    public SymbolHit? FindSymbolAt(LspPosition position)
    {
        var offset = Positions.ToOffset(position);
        (Symbol Symbol, TextSpan Span)? best = null;

        foreach (var entry in References)
        {
            if (!ContainsCaret(entry.Span, offset))
            {
                continue;
            }

            if (best is null || entry.Span.Length < best.Value.Span.Length)
            {
                best = (entry.Symbol, entry.Span);
            }
        }

        foreach (var pair in Declarations)
        {
            if (!ContainsCaret(pair.Value, offset))
            {
                continue;
            }

            if (best is null || pair.Value.Length < best.Value.Span.Length)
            {
                best = (pair.Key, pair.Value);
            }
        }

        if (best is null)
        {
            return null;
        }

        var declaration = Declarations.TryGetValue(best.Value.Symbol, out var span) ? span : best.Value.Span;
        return new SymbolHit(best.Value.Symbol, best.Value.Span, declaration);
    }

    public IReadOnlyList<TextSpan> FindAllSpans(Symbol symbol)
    {
        var spans = new HashSet<TextSpan>();
        if (Declarations.TryGetValue(symbol, out var declaration))
        {
            spans.Add(declaration);
        }

        foreach (var entry in References)
        {
            if (ReferenceEquals(entry.Symbol, symbol))
            {
                spans.Add(entry.Span);
            }
        }

        return spans.OrderBy(span => span.Start).ToArray();
    }

    public IReadOnlyList<CompletionItemModel> CompleteAt(LspPosition position)
    {
        var offset = Positions.ToOffset(position);
        var (_, prefix) = ReadPrefix(File.Text.ToString(), offset);
        var items = new Dictionary<string, CompletionItemModel>(StringComparer.Ordinal);

        foreach (var keyword in Keywords.All)
        {
            if (MatchesPrefix(keyword.Text, prefix))
            {
                items[keyword.Text] = new CompletionItemModel(keyword.Text, CompletionKind.Keyword, keyword.Text);
            }
        }

        foreach (var pair in Declarations)
        {
            if (pair.Value.Start >= offset)
            {
                continue;
            }

            if (!MatchesPrefix(pair.Key.Name, prefix))
            {
                continue;
            }

            items[pair.Key.Name] = ToCompletion(pair.Key);
        }

        foreach (var function in Bound.Functions)
        {
            if (MatchesPrefix(function.Name, prefix))
            {
                items[function.Name] = ToCompletion(function);
            }
        }

        return items.Values.OrderBy(item => item.Label, StringComparer.Ordinal).ToArray();
    }

    public HoverInfo? HoverAt(LspPosition position)
    {
        var offset = Positions.ToOffset(position);
        var keyword = KeywordAt(offset);
        if (keyword is not null)
        {
            var span = KeywordSpanAt(offset) ?? new TextSpan(offset, 0);
            return new HoverInfo(keyword, "anahtar kelime", keyword, Positions.ToRange(span));
        }

        var hit = FindSymbolAt(position);
        if (hit is null)
        {
            return null;
        }

        var typeName = TypeDisplay(hit.Symbol);
        var kind = hit.Symbol.Kind switch
        {
            SymbolKind.Variable => "değişken",
            SymbolKind.Parameter => "parametre",
            SymbolKind.Function => "fonksiyon",
            SymbolKind.Field => "alan",
            SymbolKind.Type => "tip",
            _ => hit.Symbol.Kind.ToString().ToLowerInvariant(),
        };
        var signature = hit.Symbol is FunctionSymbol function
            ? FormatSignature(function)
            : $"{hit.Symbol.Name}: {typeName}";
        return new HoverInfo(hit.Symbol.Name, kind, typeName, Positions.ToRange(hit.Span), signature);
    }

    public LspRange? GoToDefinition(LspPosition position)
    {
        var hit = FindSymbolAt(position);
        return hit is null ? null : Positions.ToRange(hit.DeclarationSpan);
    }

    public IReadOnlyList<LspRange> FindReferences(LspPosition position)
    {
        var hit = FindSymbolAt(position);
        return hit is null ? [] : FindAllSpans(hit.Symbol).Select(Positions.ToRange).ToArray();
    }

    public RenameResult? Rename(LspPosition position, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        if (newName.Length == 0 || Keywords.TryMatch(newName.AsSpan(), out _))
        {
            return null;
        }

        var hit = FindSymbolAt(position);
        if (hit is null)
        {
            return null;
        }

        var source = File.Text.ToString();
        var spans = FindAllSpans(hit.Symbol)
            .Where(span => !IsInsideStringLiteral(source, span))
            .OrderByDescending(span => span.Start)
            .ToArray();
        var edited = source;
        foreach (var span in spans)
        {
            edited = string.Concat(edited.AsSpan(0, span.Start), newName, edited.AsSpan(span.End));
        }

        return new RenameResult(edited, spans.Select(Positions.ToRange).Reverse().ToArray());
    }

    public SignatureHelpModel? SignatureHelp(LspPosition position)
    {
        var offset = Positions.ToOffset(position);
        var call = FindInvocation(Root, offset);
        if (call is null)
        {
            return null;
        }

        FunctionSymbol? function = null;
        if (call.Expression is IdentifierExpressionSyntax id)
        {
            var name = TokenText(id.Name);
            function = Bound.Functions.FirstOrDefault(item => item.Name == name);
        }

        if (function is null)
        {
            var symbol = FindSymbolCovering(call.Expression.Span);
            function = symbol as FunctionSymbol;
            if (function is null && symbol is VariableSymbol { Callable: { } callable })
            {
                function = callable;
            }
        }

        if (function is null)
        {
            return null;
        }

        var active = CountCommasBefore(call.Arguments, offset);
        var parameters = function.Parameters
            .Select(parameter => $"{parameter.Name}: {parameter.Type.DisplayName}")
            .ToArray();
        return new SignatureHelpModel(
            FormatSignature(function),
            parameters,
            parameters.Length == 0 ? 0 : Math.Clamp(active, 0, parameters.Length - 1));
    }

    public IReadOnlyList<DocumentSymbolModel> DocumentSymbols()
    {
        var symbols = new List<DocumentSymbolModel>();
        foreach (var member in Root.Members)
        {
            switch (member)
            {
                case FunctionDeclarationSyntax function:
                    symbols.Add(new DocumentSymbolModel(TokenText(function.Name), "fonksiyon", Positions.ToRange(function.Name.Span)));
                    break;
                case VariableDeclarationSyntax variable:
                    foreach (var name in variable.Names)
                    {
                        symbols.Add(new DocumentSymbolModel(TokenText(name), "değişken", Positions.ToRange(name.Span)));
                    }

                    break;
                case ModelDeclarationSyntax model:
                    symbols.Add(new DocumentSymbolModel(TokenText(model.Name), "model", Positions.ToRange(model.Name.Span)));
                    break;
                case ClassDeclarationSyntax cls:
                    symbols.Add(new DocumentSymbolModel(TokenText(cls.Name), "sınıf", Positions.ToRange(cls.Name.Span)));
                    break;
            }
        }

        return symbols;
    }

    private string TokenText(SyntaxToken token) =>
        token.Span.Length == 0 ? string.Empty : File.Text.ToString(token.Span);

    private Symbol? FindSymbolCovering(TextSpan span)
    {
        foreach (var entry in References)
        {
            if (entry.Span.Start == span.Start || span.Contains(entry.Span) || entry.Span.Contains(span.Start))
            {
                return entry.Symbol;
            }
        }

        return null;
    }

    private string? KeywordAt(int offset)
    {
        var span = KeywordSpanAt(offset);
        if (span is null)
        {
            return null;
        }

        var word = File.Text.ToString(span.Value);
        return Keywords.TryMatch(word.AsSpan(), out _) ? word : null;
    }

    private TextSpan? KeywordSpanAt(int offset)
    {
        var text = File.Text.ToString();
        if (offset < 0 || offset > text.Length)
        {
            return null;
        }

        var start = offset;
        while (start > 0 && IsIdentChar(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && IsIdentChar(text[end]))
        {
            end++;
        }

        return end > start ? TextSpan.FromBounds(start, end) : null;
    }

    private static bool ContainsCaret(TextSpan span, int offset) =>
        offset >= span.Start && offset <= span.End && span.Length > 0;

    private static void IndexSyntaxDeclarations(
        SourceFile file,
        CompilationUnitSyntax root,
        BoundCompilation bound,
        ImmutableDictionary<Symbol, TextSpan>.Builder declarations)
    {
        foreach (var member in root.Members)
        {
            IndexMemberDeclarations(file, member, bound, declarations);
        }
    }

    private static void IndexMemberDeclarations(
        SourceFile file,
        SyntaxNode member,
        BoundCompilation bound,
        ImmutableDictionary<Symbol, TextSpan>.Builder declarations)
    {
        switch (member)
        {
            case FunctionDeclarationSyntax function:
            {
                var name = TextOf(file, function.Name.Span);
                var symbol = bound.Functions.FirstOrDefault(item => item.Name == name);
                if (symbol is not null)
                {
                    declarations[symbol] = function.Name.Span;
                    for (var i = 0; i < function.Parameters.Length && i < symbol.Parameters.Count; i++)
                    {
                        declarations[symbol.Parameters[i]] = function.Parameters[i].Name.Span;
                    }
                }

                break;
            }
            case ClassDeclarationSyntax cls:
                foreach (var child in cls.Members)
                {
                    IndexMemberDeclarations(file, child, bound, declarations);
                }

                break;
        }
    }

    private static void IndexBound(
        IEnumerable<BoundNode> nodes,
        ImmutableDictionary<Symbol, TextSpan>.Builder declarations,
        ImmutableArray<(Symbol, TextSpan, bool)>.Builder references)
    {
        foreach (var node in nodes)
        {
            if (node.Symbol is not null)
            {
                if (node.Kind == BoundKind.Local)
                {
                    declarations[node.Symbol] = node.Span;
                    references.Add((node.Symbol, node.Span, true));
                }
                else if (node.Kind is BoundKind.Name or BoundKind.Call or BoundKind.Member)
                {
                    references.Add((node.Symbol, node.Span, false));
                }
            }

            if (!node.Children.IsDefaultOrEmpty)
            {
                IndexBound(node.Children, declarations, references);
            }
        }
    }

    private static string TextOf(SourceFile file, TextSpan span) =>
        span.Length == 0 ? string.Empty : file.Text.ToString(span);

    private static CompletionItemModel ToCompletion(Symbol symbol)
    {
        var kind = symbol.Kind switch
        {
            SymbolKind.Variable => CompletionKind.Variable,
            SymbolKind.Parameter => CompletionKind.Parameter,
            SymbolKind.Function => CompletionKind.Function,
            SymbolKind.Type => CompletionKind.Type,
            SymbolKind.Field => CompletionKind.Field,
            _ => CompletionKind.Text,
        };
        var detail = symbol switch
        {
            VariableSymbol variable => variable.Type.DisplayName,
            ParameterSymbol parameter => parameter.Type.DisplayName,
            FunctionSymbol function => FormatSignature(function),
            TypeSymbol type => type.DisplayName,
            FieldSymbol field => field.Type.DisplayName,
            _ => symbol.Kind.ToString(),
        };
        return new CompletionItemModel(symbol.Name, kind, detail);
    }

    private static string TypeDisplay(Symbol symbol) => symbol switch
    {
        VariableSymbol variable => variable.Type.DisplayName,
        ParameterSymbol parameter => parameter.Type.DisplayName,
        FunctionSymbol function => function.ReturnType.DisplayName,
        FieldSymbol field => field.Type.DisplayName,
        TypeSymbol type => type.DisplayName,
        _ => string.Empty,
    };

    private static string FormatSignature(FunctionSymbol function)
    {
        var parameters = string.Join(", ", function.Parameters.Select(parameter => $"{parameter.Name}: {parameter.Type.DisplayName}"));
        var returns = function.ReturnType is UnitTypeSymbol ? string.Empty : $": {function.ReturnType.DisplayName}";
        return $"{function.Name}({parameters}){returns}";
    }

    private static (int Start, string Prefix) ReadPrefix(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset;
        while (start > 0 && IsIdentChar(text[start - 1]))
        {
            start--;
        }

        return (start, text[start..offset]);
    }

    private static bool MatchesPrefix(string name, string prefix) =>
        prefix.Length == 0 || name.StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsIdentChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static bool IsInsideStringLiteral(string source, TextSpan span)
    {
        var inString = false;
        for (var i = 0; i < span.Start && i < source.Length; i++)
        {
            var ch = source[i];
            if (ch == '"' && (i == 0 || source[i - 1] != '\\'))
            {
                inString = !inString;
            }
        }

        return inString;
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode node, int offset)
    {
        InvocationExpressionSyntax? best = null;
        Walk(node);
        return best;

        void Walk(SyntaxNode current)
        {
            if (current is InvocationExpressionSyntax call &&
                call.Arguments.Span.Start <= offset &&
                offset <= call.Arguments.Span.End)
            {
                if (best is null || call.Span.Length <= best.Span.Length)
                {
                    best = call;
                }
            }

            foreach (var child in current.Children)
            {
                Walk(child);
            }
        }
    }

    private static int CountCommasBefore(ArgumentListSyntax arguments, int offset)
    {
        var count = 0;
        foreach (var argument in arguments.Arguments)
        {
            if (argument.Span.End < offset)
            {
                count++;
            }
        }

        return count;
    }
}

public sealed class ReferenceEqualityComparer : IEqualityComparer<Symbol>
{
    public static ReferenceEqualityComparer Instance { get; } = new();

    public bool Equals(Symbol? x, Symbol? y) => ReferenceEquals(x, y);

    public int GetHashCode(Symbol obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}

public sealed record SymbolHit(Symbol Symbol, TextSpan Span, TextSpan DeclarationSpan);

public sealed record ToolingDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    LspRange Range,
    string Suggestion);

public enum CompletionKind
{
    Text,
    Keyword,
    Variable,
    Parameter,
    Function,
    Type,
    Field,
}

public sealed record CompletionItemModel(string Label, CompletionKind Kind, string Detail);

public sealed record HoverInfo(string Name, string Kind, string TypeName, LspRange Range, string? Signature = null);

public sealed record RenameResult(string NewText, IReadOnlyList<LspRange> Ranges);

public sealed record SignatureHelpModel(string Label, IReadOnlyList<string> Parameters, int ActiveParameter);

public sealed record DocumentSymbolModel(string Name, string Kind, LspRange Range);
