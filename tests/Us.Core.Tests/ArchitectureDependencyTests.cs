namespace Us.Core.Tests;

public class ArchitectureDependencyTests
{
    [Fact]
    public void ReferencesMatchTheArchitectureTableAndDoNotCycle()
    {
        var root = FindRoot();
        var expected = new Dictionary<string, string[]>
        {
            ["Us.Core"] = [],
            ["Us.Lexer"] = ["Us.Core"],
            ["Us.Parser"] = ["Us.Core", "Us.Lexer"],
            ["Us.Semantics"] = ["Us.Core", "Us.Lexer", "Us.Parser"],
            ["Us.Runtime"] = [],
            ["Us.Data.SqlServer"] = ["Us.Core", "Us.Runtime"],
            ["Us.Hosting"] = ["Us.Runtime"],
            ["Us.Interpreter"] = ["Us.Core", "Us.Data.SqlServer", "Us.Hosting", "Us.Runtime", "Us.Semantics"],
            ["Us.Compiler"] = ["Us.Core", "Us.Data.SqlServer", "Us.Hosting", "Us.Parser", "Us.Runtime", "Us.Semantics"],
            ["Us.Tooling"] = ["Us.Core", "Us.Lexer", "Us.Parser", "Us.Semantics"],
            ["Us.Cli"] = ["Us.Compiler", "Us.Core", "Us.Data.SqlServer", "Us.Interpreter", "Us.Tooling"],
        };

        var graph = expected.Keys.ToDictionary(
            name => name,
            name => ReadReferences(Path.Combine(root, "src", name, $"{name}.csproj")));

        Assert.Empty(graph["Us.Runtime"]);
        foreach (var (name, references) in expected)
        {
            Assert.Equal(references.OrderBy(item => item), graph[name].OrderBy(item => item));
        }

        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();
        foreach (var name in graph.Keys)
        {
            Visit(name, graph, visiting, visited);
        }
    }

    private static void Visit(
        string name,
        IReadOnlyDictionary<string, string[]> graph,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(name))
        {
            return;
        }

        Assert.True(visiting.Add(name), name);
        foreach (var reference in graph[name])
        {
            Visit(reference, graph, visiting, visited);
        }

        visiting.Remove(name);
        visited.Add(name);
    }

    private static string[] ReadReferences(string projectPath)
    {
        var document = System.Xml.Linq.XDocument.Load(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
            .ToArray();
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
