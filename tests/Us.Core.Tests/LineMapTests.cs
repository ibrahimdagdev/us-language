namespace Us.Core.Tests;

public class LineMapTests
{
    [Theory]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb", 2)]
    [InlineData("a\rb", 2)]
    [InlineData("a\r\n\nb", 3)]
    public void CountsLineBreaks(string text, int lines)
    {
        Assert.Equal(lines, LineMap.From(SourceText.From(text)).LineCount);
    }

    [Fact]
    public void MapsOffsetToOneBasedLineAndColumn()
    {
        var map = LineMap.From(SourceText.From("İstanbul\nşehir"));
        var first = map.GetLineAndColumn(0);
        Assert.Equal((1, 1), first);
        var newline = map.GetLineAndColumn(8);
        Assert.Equal((1, 9), newline);
        var second = map.GetLineAndColumn(9);
        Assert.Equal((2, 1), second);
    }

    [Fact]
    public void TrailingBreakOpensTheNextLine()
    {
        var map = LineMap.From(SourceText.From("a\n"));
        Assert.Equal(2, map.LineCount);
        Assert.Equal((2, 1), map.GetLineAndColumn(2));
    }
}
