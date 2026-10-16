namespace Us.Core.Tests;

public class UnicodeSourceTests
{
    [Fact]
    public void IstanbulColumnsUseUtf16Units()
    {
        const string word = "İstanbul";
        var file = SourceFile.FromText("ana.us", word);
        for (var i = 0; i < word.Length; i++)
        {
            var (line, column) = file.Lines.GetLineAndColumn(i);
            Assert.Equal(1, line);
            Assert.Equal(i + 1, column);
            Assert.Equal(word[i], file.Text[i]);
        }
    }

    [Fact]
    public void CrLfIsOneBreakAndMixedBreaksDoNotInflateLines()
    {
        var file = SourceFile.FromText("ana.us", "bir\r\niki\nüç\rdört");
        Assert.Equal(4, file.Lines.LineCount);
        Assert.Equal((2, 1), file.Lines.GetLineAndColumn(5));
        Assert.Equal("iki", file.Text.ToString(new TextSpan(5, 3)));
    }
}
