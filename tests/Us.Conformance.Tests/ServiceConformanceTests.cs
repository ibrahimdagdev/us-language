using Us.Core;
using Us.Interpreter;

namespace Us.Conformance.Tests;

public class ServiceConformanceTests
{
    [Fact]
    public void FixturesMatchLifetimesChainCycleAndAsync()
    {
        var root = Path.Combine(FindRoot(), "tests", "conformance", "services");
        Assert.Equal("tamam", Line(root, "service-basic.us"));
        Assert.Equal("1", Line(root, "service-transient.us"));
        Assert.Equal("1", Line(root, "service-scoped.us"));
        Assert.Equal("1", Line(root, "service-singleton.us"));
        Assert.Equal("3", Line(root, "service-chain.us"));
        Assert.Equal("7", Line(root, "service-async.us"));

        var cycle = Run(root, "service-cycle.us");
        Assert.Contains(cycle.Diagnostics, item => item.Code == DiagnosticCodes.ServiceCycle && item.Message == "A → B → A");

        var face = Run(root, "service-interface.us");
        Assert.Contains(face.Diagnostics, item => item.Code == DiagnosticCodes.ServiceMissing);
    }

    private static string Line(string root, string name)
    {
        var result = Run(root, name);
        Assert.True(result.Evaluated && result.Diagnostics.Count == 0, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        return Assert.Single(result.Output.Lines);
    }

    private static ExecutionResult Run(string root, string name)
    {
        var path = Path.Combine(root, name);
        return ProgramRunner.RunText(path, File.ReadAllText(path));
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
