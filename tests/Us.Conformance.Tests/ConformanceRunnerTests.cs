namespace Us.Conformance.Tests;

public class ConformanceRunnerTests
{
    [Fact]
    public void EmptyDirectorySucceeds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "us-conformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var result = ConformanceRunner.Run(directory);
        Assert.Equal(0, result.CaseCount);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ParserPackageSucceedsWithoutExecuting()
    {
        var root = FindRoot();
        var result = ConformanceRunner.Run(Path.Combine(root, "tests", "conformance"));
        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal(71, result.CaseCount);
    }

    [Fact]
    public void FormatNoteNamesTheFourSlots()
    {
        var root = FindRoot();
        var note = File.ReadAllText(Path.Combine(root, "tests", "conformance", "FORMAT.txt"));
        Assert.Contains("valid.us", note, StringComparison.Ordinal);
        Assert.Contains("invalid.us", note, StringComparison.Ordinal);
        Assert.Contains("diagnostics.txt", note, StringComparison.Ordinal);
        Assert.Contains("output.txt", note, StringComparison.Ordinal);
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
