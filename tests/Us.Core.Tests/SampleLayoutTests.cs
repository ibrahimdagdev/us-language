namespace Us.Core.Tests;

public class SampleLayoutTests
{
    [Fact]
    public void RoadmapSampleDirectoriesExist()
    {
        var root = FindRoot();
        string[] names =
        [
            "Merhaba",
            "HesapMakinesi",
            "KontrolAkışı",
            "Koleksiyonlar",
            "Modeller",
            "OOP",
            "Async",
            "Eşİşlem",
            "Json",
            "Dosya",
            "Testler",
            "WebApi",
            "VeriErişimi",
        ];

        foreach (var name in names)
        {
            var path = Path.Combine(root, "samples", name);
            Assert.True(Directory.Exists(path), name);
            Assert.True(File.Exists(Path.Combine(path, ".gitkeep")), name);
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
