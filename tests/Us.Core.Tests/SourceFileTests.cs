using System.Text;

namespace Us.Core.Tests;

public class SourceFileTests
{
    [Fact]
    public void ReadsTurkishUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("çalışıyor");
        var file = SourceFile.FromUtf8("ana.us", bytes);
        Assert.Equal("çalışıyor", file.Text.ToString());
        Assert.Empty(file.EncodingFaults);
    }

    [Fact]
    public void SkipsBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("ad")).ToArray();
        var file = SourceFile.FromUtf8("ana.us", bytes);
        Assert.Equal("ad", file.Text.ToString());
        Assert.Empty(file.EncodingFaults);
    }

    [Fact]
    public void RecordsInvalidByteOffset()
    {
        var file = SourceFile.FromUtf8("ana.us", [0xFF, (byte)'A']);
        Assert.Equal("\uFFFDA", file.Text.ToString());
        var fault = Assert.Single(file.EncodingFaults);
        Assert.Equal(0, fault.ByteOffset);
        Assert.Equal(0, fault.TextOffset);
        var diagnostic = Assert.Single(file.GetEncodingDiagnostics());
        Assert.Equal(DiagnosticCodes.InvalidUtf8, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(1, diagnostic.Column);
    }
}
