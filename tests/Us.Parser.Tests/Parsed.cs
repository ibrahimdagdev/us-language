using Us.Core;
using Us.Parser;

namespace Us.Parser.Tests;

public static class Parsed
{
    public static ParseResult File(string text) =>
        Parser.Parse(SourceFile.FromText("deneme.us", text.Replace("\r\n", "\n", StringComparison.Ordinal)));

    public static ParseResult Clean(string text)
    {
        var result = File(text);
        Assert.False(result.Diagnostics.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.ToImmutableArray().Select(item => $"{item.Code}:{item.Line}:{item.Column} {item.Message}")));
        return result;
    }
}
