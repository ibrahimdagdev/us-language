using Us.Core;
using Us.Parser;
using Us.Tooling;
using Us.Tooling.Lsp;

namespace Us.Tooling.Tests;

public class LspDiagnosticTests
{
    [Fact]
    public void PublishesUs1021OnChannelAndExposesParserRoot()
    {
        var channel = new MemoryLspChannel();
        var server = new UsLanguageServer(channel);
        server.OpenWorkspace(Path.GetTempPath());
        var uri = "file:///workspace/hata.us";
        server.DidOpen(uri, "yaş: tamsayı = \"38\"\n");

        Assert.Contains(channel.LastDiagnostics, item => item.Code == DiagnosticCodes.InitializerMismatch);
        Assert.NotNull(server.LastRoot);
        Assert.IsAssignableFrom<CompilationUnitSyntax>(server.LastRoot);
        Assert.Equal(DiagnosticSeverity.Error, channel.LastDiagnostics.First(item => item.Code == DiagnosticCodes.InitializerMismatch).Severity);
        Assert.False(string.IsNullOrWhiteSpace(channel.LastDiagnostics.First(item => item.Code == DiagnosticCodes.InitializerMismatch).Message));
    }

    [Fact]
    public void IncompleteSourceDoesNotThrow()
    {
        var server = new UsLanguageServer();
        var exception = Record.Exception(() => server.DidOpen("incomplete.us", "fonksiyon topla(a: tamsayı,"));
        Assert.Null(exception);
        Assert.NotNull(server.LastRoot);
    }

    [Fact]
    public void TurkishUnicodePositionsMapRoundTrip()
    {
        var source = "ad: metin = \"İıŞşĞğÇçÖöÜü\"\n";
        var analysis = DocumentAnalysis.Analyze("unicode.us", source);
        var span = analysis.Root.Members[0].Span;
        var range = analysis.Positions.ToRange(span);
        var back = analysis.Positions.ToSpan(range);
        Assert.Equal(span.Start, back.Start);
        Assert.Equal(span.End, back.End);

        var offset = source.IndexOf('İ', StringComparison.Ordinal);
        var position = analysis.Positions.ToPosition(offset);
        Assert.Equal(0, position.Line);
        Assert.Equal(analysis.Positions.ToOffset(position), offset);
    }
}
