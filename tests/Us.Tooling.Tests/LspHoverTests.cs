using Us.Tooling;
using Us.Tooling.Lsp;

namespace Us.Tooling.Tests;

public class LspHoverTests
{
    [Fact]
    public void CompletesVariableAdAndHoversMetinType()
    {
        var source = """
            ad: metin = "Ali"
            syaz ad
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var server = new UsLanguageServer();
        var uri = "hover.us";
        server.DidOpen(uri, source);

        var adOffset = source.LastIndexOf("ad", StringComparison.Ordinal);
        var position = server.GetDocument(uri)!.Positions.ToPosition(adOffset);
        var completions = server.Completion(uri, position);
        Assert.Contains(completions, item => item.Label == "ad");

        var hover = server.Hover(uri, position);
        Assert.NotNull(hover);
        Assert.Equal("ad", hover!.Name);
        Assert.Equal("metin", hover.TypeName);
    }

    [Fact]
    public void KeywordHoverUsesSpecShortName()
    {
        var source = "eğer doğru\n{\n}\n";
        var server = new UsLanguageServer();
        var uri = "kw.us";
        server.DidOpen(uri, source);
        var position = server.GetDocument(uri)!.Positions.ToPosition(0);
        var hover = server.Hover(uri, position);
        Assert.NotNull(hover);
        Assert.Equal("eğer", hover!.Name);
        Assert.Equal("anahtar kelime", hover.Kind);
    }
}
