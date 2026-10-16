using Us.Parser;

namespace Us.Tooling.Lsp;

/// <summary>
/// In-process US language server. Transport is memory-backed for tests; no second parser.
/// </summary>
public sealed class UsLanguageServer
{
    private readonly MemoryLspChannel _channel;
    private readonly Dictionary<string, DocumentAnalysis> _documents = new(StringComparer.OrdinalIgnoreCase);
    private string? _workspaceRoot;

    public UsLanguageServer(MemoryLspChannel? channel = null)
    {
        _channel = channel ?? new MemoryLspChannel();
    }

    public MemoryLspChannel Channel => _channel;

    public string? WorkspaceRoot => _workspaceRoot;

    public CompilationUnitSyntax? LastRoot { get; private set; }

    public void OpenWorkspace(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _workspaceRoot = rootPath;
    }

    public DocumentAnalysis DidOpen(string uri, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(text);
        var analysis = SafeAnalyze(uri, text);
        _documents[uri] = analysis;
        LastRoot = analysis.Root;
        PublishDiagnostics(uri, analysis);
        return analysis;
    }

    public DocumentAnalysis DidChange(string uri, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentNullException.ThrowIfNull(text);
        var analysis = SafeAnalyze(uri, text);
        _documents[uri] = analysis;
        LastRoot = analysis.Root;
        PublishDiagnostics(uri, analysis);
        return analysis;
    }

    public void DidClose(string uri)
    {
        _documents.Remove(uri);
        _channel.PublishDiagnostics(uri, []);
    }

    public DocumentAnalysis? GetDocument(string uri) =>
        _documents.TryGetValue(uri, out var document) ? document : null;

    public IReadOnlyList<ToolingDiagnostic> GetDiagnostics(string uri) =>
        GetDocument(uri)?.GetDiagnostics() ?? [];

    public IReadOnlyList<CompletionItemModel> Completion(string uri, LspPosition position) =>
        GetDocument(uri)?.CompleteAt(position) ?? [];

    public HoverInfo? Hover(string uri, LspPosition position) =>
        GetDocument(uri)?.HoverAt(position);

    public LspRange? Definition(string uri, LspPosition position) =>
        GetDocument(uri)?.GoToDefinition(position);

    public IReadOnlyList<LspRange> References(string uri, LspPosition position) =>
        GetDocument(uri)?.FindReferences(position) ?? [];

    public RenameResult? Rename(string uri, LspPosition position, string newName) =>
        GetDocument(uri)?.Rename(position, newName);

    public SignatureHelpModel? SignatureHelp(string uri, LspPosition position) =>
        GetDocument(uri)?.SignatureHelp(position);

    public IReadOnlyList<DocumentSymbolModel> DocumentSymbols(string uri) =>
        GetDocument(uri)?.DocumentSymbols() ?? [];

    private void PublishDiagnostics(string uri, DocumentAnalysis analysis)
    {
        _channel.PublishDiagnostics(uri, analysis.GetDiagnostics());
    }

    private static DocumentAnalysis SafeAnalyze(string uri, string text)
    {
        try
        {
            return DocumentAnalysis.Analyze(uri, text);
        }
        catch
        {
            return DocumentAnalysis.Analyze(uri, string.Empty);
        }
    }
}

public sealed class MemoryLspChannel
{
    private readonly List<(string Uri, IReadOnlyList<ToolingDiagnostic> Diagnostics)> _publications = [];

    public IReadOnlyList<(string Uri, IReadOnlyList<ToolingDiagnostic> Diagnostics)> Publications => _publications;

    public IReadOnlyList<ToolingDiagnostic> LastDiagnostics { get; private set; } = [];

    public void PublishDiagnostics(string uri, IReadOnlyList<ToolingDiagnostic> diagnostics)
    {
        LastDiagnostics = diagnostics;
        _publications.Add((uri, diagnostics));
    }
}
