using Us.Core;
using Us.Parser;
using Us.Tooling;

namespace Us.Tooling.Tests;

public class FormatterTests
{
    [Fact]
    public void FormatsIfWithFourSpaceIndentAndBracesOnOwnLines()
    {
        var source = "eğer yaş>=18{syaz \"Yetişkin\"}değilse{syaz \"Çocuk\"}";
        var formatted = Formatter.Format(source);
        var expected = """
            eğer yaş >= 18
            {
                syaz "Yetişkin"
            }
            değilse
            {
                syaz "Çocuk"
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected.TrimEnd() + "\n", formatted.TrimEnd() + "\n");
        Assert.DoesNotContain(";", formatted, StringComparison.Ordinal);
        Assert.Contains("\n", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatIsIdempotent()
    {
        var source = """
            eğer doğru
            {
                syaz "a"
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var once = Formatter.Format(source);
        var twice = Formatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void RoundTripParseAfterFormat()
    {
        var source = """
            ad: metin = "İbrahim"
            eğer doğru
            {
                syaz "Merhaba {ad}"
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var formatted = Formatter.Format(source);
        var parsed = global::Us.Parser.Parser.Parse(SourceFile.FromText("x.us", formatted));
        Assert.False(parsed.Diagnostics.HasErrors);
        Assert.True(parsed.Root.Members.Length >= 2);
    }
}

public class FormatterTriviaTests
{
    [Fact]
    public void PreservesLineCommentsAndStringContents()
    {
        var source = """
            // merhaba
            ad: metin = "  boşluk  "
            syaz ad
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var formatted = Formatter.Format(source);
        Assert.Contains("// merhaba", formatted, StringComparison.Ordinal);
        Assert.Contains("\"  boşluk  \"", formatted, StringComparison.Ordinal);
        Assert.Contains("İbrahim", Formatter.Format("syaz \"İbrahim\"\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesBlockComments()
    {
        var source = "/* blok */\nsyaz \"x\"\n";
        var formatted = Formatter.Format(source);
        Assert.Contains("/* blok */", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void IncompleteSourceDoesNotThrow()
    {
        var exception = Record.Exception(() => Formatter.Format("fonksiyon topla(a: tamsayı,"));
        Assert.Null(exception);
    }
}
