namespace Us.Core.Tests;

public class DesignDecisionTests
{
    [Fact]
    public void LanguageSpecIsPresentAndNamesUs()
    {
        var root = FindRoot();
        var spec = File.ReadAllText(Path.Combine(root, "docs", "language-spec.md"));
        Assert.Contains("US", spec, StringComparison.Ordinal);
        Assert.Contains("Türkçe", spec, StringComparison.Ordinal);
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
