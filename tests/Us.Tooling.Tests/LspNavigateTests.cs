using Us.Tooling.Lsp;

namespace Us.Tooling.Tests;

public class LspNavigateTests
{
    [Fact]
    public void GoToDefinitionAndReferencesUseSameSymbol()
    {
        var source = """
            ad: metin = "Ali"
            syaz ad
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = new UsLanguageServer();
        var uri = "nav.us";
        server.DidOpen(uri, source);
        var doc = server.GetDocument(uri)!;
        var useOffset = source.LastIndexOf("ad", StringComparison.Ordinal);
        var usePos = doc.Positions.ToPosition(useOffset);

        var definition = server.Definition(uri, usePos);
        Assert.NotNull(definition);
        Assert.Equal(0, definition!.Value.Start.Line);

        var references = server.References(uri, usePos);
        Assert.True(references.Count >= 2);
    }

    [Fact]
    public void RenameChangesOnlySymbolSpansNotStringLiteral()
    {
        var source = """
            ad: metin = "ad"
            syaz ad
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = new UsLanguageServer();
        var uri = "rename.us";
        server.DidOpen(uri, source);
        var doc = server.GetDocument(uri)!;
        var useOffset = source.LastIndexOf("ad", StringComparison.Ordinal);
        var result = server.Rename(uri, doc.Positions.ToPosition(useOffset), "isim");
        Assert.NotNull(result);
        Assert.Contains("isim: metin = \"ad\"", result!.NewText, StringComparison.Ordinal);
        Assert.Contains("syaz isim", result.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain("isim: metin = \"isim\"", result.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public void SignatureHelpShowsTwoParameters()
    {
        var source = """
            fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı => a + b
            sonuç: tamsayı = topla(1, 
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = new UsLanguageServer();
        var uri = "sig.us";
        var exception = Record.Exception(() => server.DidOpen(uri, source));
        Assert.Null(exception);
        var doc = server.GetDocument(uri)!;
        var offset = source.LastIndexOf(',') + 1;
        var help = server.SignatureHelp(uri, doc.Positions.ToPosition(offset));
        Assert.NotNull(help);
        Assert.Equal(2, help!.Parameters.Count);
        Assert.Contains(help.Parameters, item => item.Contains("a:", StringComparison.Ordinal));
        Assert.Contains(help.Parameters, item => item.Contains("b:", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentSymbolsListFunction()
    {
        var source = "fonksiyon toplam(): tamsayı => 1\n";
        var server = new UsLanguageServer();
        var uri = "sym.us";
        server.DidOpen(uri, source);
        var symbols = server.DocumentSymbols(uri);
        Assert.Contains(symbols, item => item.Name == "toplam" && item.Kind == "fonksiyon");
    }
}
