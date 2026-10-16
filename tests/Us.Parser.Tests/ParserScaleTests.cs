using Us.Parser;

namespace Us.Parser.Tests;

public class ParserScaleTests
{
    [Fact]
    public void DoublingLinesDoesNotExplodeNodeCount()
    {
        var small = Parsed.Clean(BuildSource(40));
        var large = Parsed.Clean(BuildSource(80));

        var smallCount = CountNodes(small.Root);
        var largeCount = CountNodes(large.Root);

        Assert.True(smallCount > 0);
        Assert.True(largeCount > smallCount);
        // İki kat satır ≈ iki kat düğüm; üst sınır üç kat.
        Assert.True(
            largeCount <= smallCount * 3,
            $"Düğüm sayısı üç katı aştı: küçük={smallCount}, büyük={largeCount}");
    }

    private static string BuildSource(int declarations)
    {
        var lines = new List<string>(declarations);
        for (var i = 0; i < declarations; i++)
        {
            lines.Add($"değer{i}: tamsayı = {i}");
        }

        return string.Join('\n', lines);
    }

    private static int CountNodes(SyntaxNode node)
    {
        var count = 1;
        foreach (var child in node.Children)
        {
            count += CountNodes(child);
        }

        return count;
    }
}
