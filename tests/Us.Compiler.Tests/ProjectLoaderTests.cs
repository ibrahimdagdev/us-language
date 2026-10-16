using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

public class ProjectLoaderTests
{
    [Fact]
    public void EntryPathComesFromManifest()
    {
        var root = CreateTempProject("""
            proje "Ornek"
            sürüm "1.0.0"
            hedef "uygulama"
            başlangıç "giris.us"
            paket "US.Json" sürüm "1.0.0"
            """, "giris.us", "syaz \"ok\"");
        try
        {
            var project = ProjectLoader.Load(root);
            Assert.False(project.HasErrors);
            Assert.Equal(Path.Combine(root, "giris.us"), project.EntryPath);
            var package = Assert.Single(project.Dependencies);
            Assert.Equal("US.Json", package.Name);
            Assert.DoesNotContain(root, Directory.EnumerateDirectories(root), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingEntryIsUS1701()
    {
        var root = CreateTempProject("""
            proje "Ornek"
            sürüm "1.0.0"
            hedef "uygulama"
            """, null, null);
        try
        {
            var project = ProjectLoader.Load(root);
            Assert.Contains(project.Diagnostics, item => item.Code == DiagnosticCodes.MissingEntry);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempProject(string manifest, string? entryName, string? entryText)
    {
        var root = Path.Combine(Path.GetTempPath(), "us-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "us.proje"), manifest.Replace("\r\n", "\n", StringComparison.Ordinal));
        if (entryName is not null && entryText is not null)
        {
            File.WriteAllText(Path.Combine(root, entryName), entryText.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        return root;
    }
}
