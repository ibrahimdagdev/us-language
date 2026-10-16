namespace Us.Conformance.Tests;

public class ConformanceSuiteTests
{
    public static TheoryData<string> FeatureFolders { get; } = new()
    {
        "degisken",
        "eger",
        "fonksiyon",
        "model",
        "sorgu",
        "hata",
        "eszaman",
        "esislem",
    };

    [Theory]
    [MemberData(nameof(FeatureFolders))]
    public void FeatureFolderPasses(string folder)
    {
        var root = FindRoot();
        var path = Path.Combine(root, "tests", "conformance", folder);
        Assert.True(Directory.Exists(path), path);
        Assert.True(File.Exists(Path.Combine(path, "valid.us")), folder + "/valid.us");
        Assert.True(File.Exists(Path.Combine(path, "invalid.us")), folder + "/invalid.us");
        Assert.True(File.Exists(Path.Combine(path, "diagnostics.txt")), folder + "/diagnostics.txt");

        var result = ConformanceRunner.Run(path);
        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal(2, result.CaseCount);
    }

    [Fact]
    public void SuiteRootContainsAllFeatureFolders()
    {
        var root = Path.Combine(FindRoot(), "tests", "conformance");
        foreach (var folder in FeatureFolders)
        {
            Assert.True(Directory.Exists(Path.Combine(root, folder)), folder);
        }
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
