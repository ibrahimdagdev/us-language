using Us.Core;
using Us.Parser;

namespace Us.Parser.Tests;

public class SyntaxNodeTests
{
    [Fact]
    public void SpanIsUnionOfChildren()
    {
        var result = Parsed.Clean("ad: metin = \"İbrahim\"");
        var declaration = Assert.IsType<VariableDeclarationSyntax>(Assert.Single(result.Root.Members));
        Assert.Equal(declaration.Names[0].Span.Start, declaration.Span.Start);
        Assert.Equal(declaration.Initializers[0].Span.End, declaration.Span.End);
        Assert.NotEqual(0, declaration.Names[0].Span.Length);
    }

    [Fact]
    public void ChildrenStayFixedAfterConstruction()
    {
        var result = Parsed.Clean("{\n}\n");
        var block = Assert.IsType<BlockSyntax>(Assert.Single(result.Root.Members));
        var count = block.Children.Count;
        Assert.Equal(2, count);
        Assert.Equal(count, block.Children.Count);
    }
}
