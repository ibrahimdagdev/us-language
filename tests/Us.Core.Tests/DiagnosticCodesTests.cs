namespace Us.Core.Tests;

public class DiagnosticCodesTests
{
    [Fact]
    public void InvalidUtf8CodeIsStableAndTurkish()
    {
        Assert.Equal("US0001", DiagnosticCodes.InvalidUtf8);
        Assert.True(DiagnosticCodes.IsWellFormed(DiagnosticCodes.InvalidUtf8));
        var diagnostic = Assert.Single(SourceFile.FromUtf8("ana.us", [0x80]).GetEncodingDiagnostics());
        Assert.Equal(DiagnosticCodes.InvalidUtf8, diagnostic.Code);
        Assert.Contains("UTF-8", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("US0002", diagnostic.Code, StringComparison.Ordinal);
    }
}
