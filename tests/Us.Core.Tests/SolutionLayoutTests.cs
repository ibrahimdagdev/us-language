namespace Us.Core.Tests;

public class SolutionLayoutTests
{
    [Fact]
    public void ProductionProjectsMatchArchitecture()
    {
        var root = FindRepositoryRoot();
        var expected = new Dictionary<string, string[]>
        {
            ["src/Us.Core/Us.Core.csproj"] = [],
            ["src/Us.Lexer/Us.Lexer.csproj"] = ["Us.Core"],
            ["src/Us.Parser/Us.Parser.csproj"] = ["Us.Core", "Us.Lexer"],
            ["src/Us.Semantics/Us.Semantics.csproj"] = ["Us.Core", "Us.Lexer", "Us.Parser"],
            ["src/Us.Runtime/Us.Runtime.csproj"] = [],
            ["src/Us.Data.SqlServer/Us.Data.SqlServer.csproj"] = ["Us.Core", "Us.Runtime"],
            ["src/Us.Hosting/Us.Hosting.csproj"] = ["Us.Runtime"],
            ["src/Us.Interpreter/Us.Interpreter.csproj"] = ["Us.Core", "Us.Data.SqlServer", "Us.Hosting", "Us.Runtime", "Us.Semantics"],
            ["src/Us.Compiler/Us.Compiler.csproj"] = ["Us.Core", "Us.Data.SqlServer", "Us.Hosting", "Us.Parser", "Us.Runtime", "Us.Semantics"],
            ["src/Us.Tooling/Us.Tooling.csproj"] = ["Us.Core", "Us.Lexer", "Us.Parser", "Us.Semantics"],
            ["src/Us.Cli/Us.Cli.csproj"] = ["Us.Compiler", "Us.Core", "Us.Data.SqlServer", "Us.Interpreter", "Us.Tooling"],
        };

        foreach (var (relative, references) in expected)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), relative);
            var actual = ReadProjectReferences(path);
            Assert.Equal(references.OrderBy(name => name), actual.OrderBy(name => name));
        }
    }

    [Fact]
    public void TestProjectsReferenceOnlyTheirLibrary()
    {
        var root = FindRepositoryRoot();
        string[] names =
        [
            "Us.Core",
            "Us.Lexer",
            "Us.Parser",
            "Us.Semantics",
            "Us.Runtime",
            "Us.Interpreter",
            "Us.Compiler",
            "Us.Cli",
            "Us.Tooling",
        ];

        foreach (var name in names)
        {
            var path = Path.Combine(root, "tests", $"{name}.Tests", $"{name}.Tests.csproj");
            Assert.True(File.Exists(path), path);
            var actual = ReadProjectReferences(path).OrderBy(item => item).ToArray();
            string[] expected = name switch
            {
                "Us.Compiler" => ["Us.Compiler", "Us.Data.SqlServer", "Us.Interpreter"],
                "Us.Runtime" => ["Us.Data.SqlServer", "Us.Hosting", "Us.Runtime"],
                _ => [name],
            };
            Assert.Equal(expected.OrderBy(item => item).ToArray(), actual);
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var document = System.Xml.Linq.XDocument.Load(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .ToArray();
    }
}
