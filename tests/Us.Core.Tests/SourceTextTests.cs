namespace Us.Core.Tests;

public class SourceTextTests
{
    [Fact]
    public void KeepsTurkishCharacters()
    {
        var text = SourceText.From("İstanbul");
        Assert.Equal(8, text.Length);
        Assert.Equal('İ', text[0]);
        Assert.Equal('ş', SourceText.From("ş")[0]);
        Assert.Equal("tan", text.ToString(new TextSpan(2, 3)));
        Assert.Equal("tan", text.Slice(new TextSpan(2, 3)).ToString());
    }

    [Fact]
    public void EmptyTextHasZeroLength()
    {
        var text = SourceText.From("");
        Assert.Equal(0, text.Length);
        Assert.Equal("", text.ToString());
    }

    [Fact]
    public void RejectsIndexAndSpanOutsideTheText()
    {
        var text = SourceText.From("ab");
        Assert.Throws<ArgumentOutOfRangeException>(() => text[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => text.ToString(new TextSpan(1, 2)));
    }
}
