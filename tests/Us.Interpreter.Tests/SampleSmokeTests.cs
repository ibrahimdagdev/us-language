using Us.Core;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Interpreter.Tests;

public class SampleSmokeTests
{
    [Theory]
    [InlineData("Merhaba", new[] { "Merhaba İbrahim" })]
    [InlineData("HesapMakinesi", new[] { "30" })]
    [InlineData("KontrolAkışı", new[] { "Yetişkin" })]
    [InlineData("Koleksiyonlar", new[] { "Ali", "Mehmet" })]
    [InlineData("Modeller", new[] { "İbrahim" })]
    [InlineData("OOP", new[] { "hav", "Köpek" })]
    [InlineData("Eşİşlem", new[] { "10" })]
    [InlineData("Json", new[] { "İbrahim", "38" })]
    [InlineData("Dosya", new[] { "İbrahim" })]
    [InlineData("VeriErişimi", new[] { "Ali", "1" })]
    public void ConsoleSamplesPrintExpectedOutput(string name, string[] expected)
    {
        var path = Path.Combine(FindRoot(), "samples", name, "ana.us");
        Assert.True(File.Exists(path), path);
        var result = ProgramRunner.Run(SourceFile.FromText(path, File.ReadAllText(path)));
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(expected, result.Output.Lines);
    }

    [Fact]
    public async Task AsyncSamplePrintsTamam()
    {
        var path = Path.Combine(FindRoot(), "samples", "Async", "ana.us");
        var result = await ProgramRunner.RunAsync(SourceFile.FromText(path, File.ReadAllText(path)));
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Tamam", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public async Task TestlerSamplePasses()
    {
        var path = Path.Combine(FindRoot(), "samples", "Testler", "ana.us");
        var result = await TestRunner.RunTextAsync(path, File.ReadAllText(path));
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(2, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public async Task WebApiSampleGetAndPostWork()
    {
        var path = Path.Combine(FindRoot(), "samples", "WebApi", "ana.us");
        var host = UsWebHost.FromText(path, File.ReadAllText(path));
        var post = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", """{"id":0,"ad":"Ali"}""", "application/json"));
        Assert.Equal(200, post.StatusCode);
        var get = await host.DispatchAsync(new UsHttpRequest("GET", "/kullanicilar/1"));
        Assert.Equal(200, get.StatusCode);
        Assert.Contains("Ali", get.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void AllRoadmapSamplesHaveSource()
    {
        var root = FindRoot();
        string[] names =
        [
            "Merhaba", "HesapMakinesi", "KontrolAkışı", "Koleksiyonlar", "Modeller", "OOP",
            "Async", "Eşİşlem", "Json", "Dosya", "Testler", "WebApi", "VeriErişimi", "BackendAcceptance",
        ];
        foreach (var name in names)
        {
            Assert.True(File.Exists(Path.Combine(root, "samples", name, "ana.us")), name);
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
