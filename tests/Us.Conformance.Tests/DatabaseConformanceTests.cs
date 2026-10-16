using Us.Core;
using Us.Interpreter;

namespace Us.Conformance.Tests;

public class DatabaseConformanceTests
{
    [Fact]
    public void FixturesMatchBasicFilterParameterAndTransactions()
    {
        var root = Path.Combine(FindRoot(), "tests", "conformance", "database");
        Assert.Equal("1", Line(root, "database-basic.us"));
        Assert.Equal("1", Line(root, "database-filter.us"));
        Assert.Equal("1", Line(root, "database-parameter.us"));
        Assert.Equal("1", Line(root, "transaction-commit.us"));
        Assert.Equal("0", Line(root, "transaction-rollback.us"));
    }

    private static string Line(string root, string name)
    {
        var path = Path.Combine(root, name);
        var result = ProgramRunner.RunText(path, File.ReadAllText(path));
        Assert.True(result.Evaluated && result.Diagnostics.Count == 0, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        return Assert.Single(result.Output.Lines);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
