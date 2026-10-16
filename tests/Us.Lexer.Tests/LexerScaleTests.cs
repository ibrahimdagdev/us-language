using System.Diagnostics;
using System.Text;
using Us.Core;
using Us.Lexer;

namespace Us.Lexer.Tests;

public class LexerScaleTests
{
    [Fact]
    public void TwoHundredThousandLinesStayLinear()
    {
        var text = BuildSource(200_000);
        var file = SourceFile.FromText("olcek.us", text);

        var first = TimeLex(file);
        var second = TimeLex(file);

        Assert.Equal(0, first.DiagnosticCount);
        Assert.Equal(0, second.DiagnosticCount);
        Assert.True(
            second.Elapsed <= first.Elapsed * 4,
            $"İkinci tarama dört kattan yavaş: birinci={first.Elapsed}, ikinci={second.Elapsed}");
    }

    private static string BuildSource(int lines)
    {
        var builder = new StringBuilder(lines * 24);
        for (var i = 0; i < lines; i++)
        {
            builder.Append("değer").Append(i).Append(": tamsayı = ").Append(i).Append('\n');
        }

        return builder.ToString();
    }

    private static (TimeSpan Elapsed, int DiagnosticCount) TimeLex(SourceFile file)
    {
        var watch = Stopwatch.StartNew();
        var result = Lexer.Tokenize(file);
        watch.Stop();
        return (watch.Elapsed, result.Diagnostics.ToImmutableArray().Length);
    }
}
