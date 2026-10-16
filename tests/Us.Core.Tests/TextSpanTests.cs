namespace Us.Core.Tests;

public class TextSpanTests
{
    [Fact]
    public void EndIsStartPlusLength()
    {
        var span = new TextSpan(2, 4);
        Assert.Equal(6, span.End);
    }

    [Fact]
    public void ContainsAndOverlaps()
    {
        var span = new TextSpan(2, 4);
        Assert.True(span.Contains(2));
        Assert.True(span.Contains(5));
        Assert.False(span.Contains(6));
        Assert.True(span.Contains(new TextSpan(3, 2)));
        Assert.True(span.Overlaps(new TextSpan(5, 3)));
        Assert.False(span.Overlaps(new TextSpan(6, 1)));
        Assert.False(span.Overlaps(new TextSpan(4, 0)));
    }

    [Fact]
    public void RejectsNegativeBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextSpan(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextSpan.FromBounds(4, 3));
    }
}
