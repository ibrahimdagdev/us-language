using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

public class ProjectManifestTests
{
    [Fact]
    public void SampleManifestFillsFiveFields()
    {
        var manifest = ProjectManifestParser.Parse("us.proje", """
            proje "Ticaret"
            sürüm "1.0.0"
            hedef "uygulama"
            başlangıç "ana.us"
            paket "US.Json" sürüm "1.0.0"
            """);
        Assert.False(manifest.HasErrors);
        Assert.Equal("Ticaret", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(ProjectTarget.Application, manifest.Target);
        Assert.Equal("ana.us", manifest.Entry);
        var package = Assert.Single(manifest.Packages);
        Assert.Equal("US.Json", package.Name);
        Assert.Equal("1.0.0", package.Version);
    }

    [Fact]
    public void UnknownKeyIsUS1700()
    {
        var manifest = ProjectManifestParser.Parse("us.proje", """
            proje "X"
            gizli "hayır"
            """);
        var diagnostic = Assert.Single(manifest.Diagnostics);
        Assert.Equal(DiagnosticCodes.UnknownProjectKey, diagnostic.Code);
        Assert.Contains("gizli", diagnostic.Message, StringComparison.Ordinal);
    }
}
