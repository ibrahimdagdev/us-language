using Us.Core;
using Us.Parser;

namespace Us.Parser.Tests;

public class ParserFixtureTests
{
    [Fact]
    public void FixturesHaveNoParserDiagnostics()
    {
        foreach (var path in Directory.GetFiles(FixtureDirectory(), "*.us"))
        {
            var result = Parser.Parse(SourceFile.FromText(path, File.ReadAllText(path)));
            Assert.False(result.Diagnostics.HasErrors, path + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics.ToImmutableArray().Select(item => $"{item.Code}:{item.Line}:{item.Column} {item.Message}")));
        }
    }

    [Theory]
    [InlineData("01-degiskenler.us", new[] { "VariableDeclaration", "VariableDeclaration", "VariableDeclaration", "VariableDeclaration", "VariableDeclaration", "VariableDeclaration", "ExpressionStatement" })]
    [InlineData("02-kontrol-akisi.us", new[] { "VariableDeclaration", "IfStatement", "RangeForStatement", "WhileStatement", "DoWhileStatement", "InfiniteLoopStatement" })]
    [InlineData("03-fonksiyonlar.us", new[] { "FunctionDeclaration", "FunctionDeclaration", "FunctionDeclaration", "FunctionDeclaration", "FunctionDeclaration", "VariableDeclaration", "ExpressionStatement" })]
    [InlineData("04-model.us", new[] { "ModelDeclaration", "VariableDeclaration", "VariableDeclaration" })]
    public void FixtureMemberKindsStayStable(string name, string[] kinds)
    {
        var result = Parser.Parse(SourceFile.FromText(name, File.ReadAllText(Path.Combine(FixtureDirectory(), name))));
        Assert.Equal(kinds, result.Root.Members.Select(member => member.Kind.ToString()).ToArray());
        var dump = SyntaxDumper.Dump(result.Root);
        Assert.Contains(kinds[0], dump, StringComparison.Ordinal);
    }

    private static string FixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "tests", "Us.Parser.Tests", "fixtures");
    }
}
