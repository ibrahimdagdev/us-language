namespace Us.Core.Tests;

public class DiagnosticTests
{
    [Fact]
    public void StoresSeverityMessageAndOptionalSuggestion()
    {
        var withSuggestion = new Diagnostic(
            "US1021",
            DiagnosticSeverity.Error,
            "Tip uyuşmuyor.",
            "ana.us",
            new TextSpan(4, 2),
            12,
            5,
            "yaş: tamsayı = 38");
        var without = new Diagnostic(
            "US1021",
            DiagnosticSeverity.Information,
            "Tip uyuşmuyor.",
            "ana.us",
            new TextSpan(4, 2),
            12,
            5,
            null);

        Assert.Equal(DiagnosticSeverity.Error, withSuggestion.Severity);
        Assert.Equal("yaş: tamsayı = 38", withSuggestion.Suggestion);
        Assert.Null(without.Suggestion);
        Assert.NotEqual(DiagnosticSeverity.Warning, withSuggestion.Severity);
    }

    [Theory]
    [InlineData("US12")]
    [InlineData("us0001")]
    [InlineData("US00001")]
    public void RejectsMalformedCode(string code)
    {
        Assert.Throws<ArgumentException>(() => new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            "ileti",
            null,
            new TextSpan(0, 1),
            1,
            1,
            null));
    }

    [Fact]
    public void RejectsEmptyMessage()
    {
        Assert.Throws<ArgumentException>(() => new Diagnostic(
            DiagnosticCodes.InvalidUtf8,
            DiagnosticSeverity.Warning,
            "  ",
            null,
            new TextSpan(0, 0),
            1,
            1,
            null));
    }
}
