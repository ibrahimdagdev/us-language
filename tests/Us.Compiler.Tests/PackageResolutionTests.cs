using System.IO.Compression;
using System.Text;
using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

public class PackageResolutionTests
{
    [Fact]
    public void ResolvesLocalPackageSourcesDeterministically()
    {
        var root = TempProject();
        try
        {
            WriteManifest(root, """
                proje "Uyg"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                paket "Util" sürüm "1.0.0"
                """);
            File.WriteAllText(Path.Combine(root, "ana.us"), "syaz Yardim.Metin()\n");
            InstallPackage(root, "Util", "1.0.0", ("lib.us", """
                fonksiyon Metin(): metin => "merhaba"
                """));

            var project = ProjectLoader.Load(root);
            Assert.False(project.HasErrors, string.Join(Environment.NewLine, project.Diagnostics.Select(item => item.Message)));
            Assert.Contains(project.SourcePaths, path => path.Replace('\\', '/').EndsWith("paketler/Util/1.0.0/lib.us", StringComparison.OrdinalIgnoreCase)
                || path.Replace('\\', '/').Contains("/paketler/Util/1.0.0/", StringComparison.OrdinalIgnoreCase));

            var first = PackageResolver.Resolve(project);
            var second = PackageResolver.Resolve(project);
            Assert.Equal(
                first.Packages.Select(item => item.IdentityKey).ToArray(),
                second.Packages.Select(item => item.IdentityKey).ToArray());
            Assert.Equal(
                first.Packages.SelectMany(item => item.SourcePaths).ToArray(),
                second.Packages.SelectMany(item => item.SourcePaths).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ConflictExactVersionsFails()
    {
        var root = TempProject();
        try
        {
            WriteManifest(root, """
                proje "Uyg"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                paket "Util" sürüm "1.0.0"
                paket "Util" sürüm "2.0.0"
                """);
            File.WriteAllText(Path.Combine(root, "ana.us"), "syaz \"x\"\n");
            var project = ProjectLoader.Load(root);
            Assert.Contains(project.Diagnostics, item => item.Code == DiagnosticCodes.PackageConflict);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DuplicateExactPackageLineIsIdempotent()
    {
        var diagnostics = new List<Diagnostic>();
        var result = PackageResolver.Resolve(
            Path.GetTempPath(),
            [
                new PackageReference("Util", "1.0.0", default),
                new PackageReference("Util", "1.0.0", default),
            ]);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DependencyCycleIsDiagnosed()
    {
        var root = TempProject();
        try
        {
            WriteManifest(root, """
                proje "Uyg"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                paket "A" sürüm "1.0.0"
                """);
            File.WriteAllText(Path.Combine(root, "ana.us"), "syaz \"x\"\n");
            InstallPackage(root, "A", "1.0.0",
                ("us.proje", """
                    proje "A"
                    sürüm "1.0.0"
                    hedef "kütüphane"
                    paket "B" sürüm "1.0.0"
                    """),
                ("a.us", "fonksiyon A(): tamsayı => 1\n"));
            InstallPackage(root, "B", "1.0.0",
                ("us.proje", """
                    proje "B"
                    sürüm "1.0.0"
                    hedef "kütüphane"
                    paket "A" sürüm "1.0.0"
                    """),
                ("b.us", "fonksiyon B(): tamsayı => 2\n"));

            var project = ProjectLoader.Load(root);
            Assert.Contains(project.Diagnostics, item => item.Code == DiagnosticCodes.PackageCycle);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MissingLocalPackageDoesNotFailSoftResolve()
    {
        var root = TempProject();
        try
        {
            WriteManifest(root, """
                proje "Uyg"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                paket "Yok" sürüm "1.0.0"
                """);
            File.WriteAllText(Path.Combine(root, "ana.us"), "syaz \"ok\"\n");
            var project = ProjectLoader.Load(root);
            Assert.DoesNotContain(project.Diagnostics, item => item.Code == DiagnosticCodes.PackageNotFound);
            Assert.False(project.HasErrors);

            var required = PackageResolver.ResolveRequired(root, project.Dependencies);
            Assert.Contains(required.Diagnostics, item => item.Code == DiagnosticCodes.PackageNotFound);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InstallFromArchiveThenCompileUsesPackageSource()
    {
        var root = TempProject();
        var archive = Path.Combine(Path.GetTempPath(), "us-util-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            WriteManifest(root, """
                proje "Uyg"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """);
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                syaz Selam()
                """);
            BuildZip(archive,
                ("us.proje", """
                    proje "SelamPaket"
                    sürüm "1.2.0"
                    hedef "kütüphane"
                    """),
                ("selam.us", """
                    fonksiyon Selam(): metin => "paketten"
                    """));

            var installed = PackageCache.InstallFromArchive(root, archive);
            Assert.True(installed.Succeeded, string.Join(Environment.NewLine, installed.Diagnostics.Select(item => item.Message)));
            Assert.Equal("SelamPaket", installed.Name);
            Assert.Equal("1.2.0", installed.Version);

            File.AppendAllText(Path.Combine(root, "us.proje"), "\npaket \"SelamPaket\" sürüm \"1.2.0\"\n");
            Assert.True(File.Exists(Path.Combine(root, "paketler", "SelamPaket", "1.2.0", "selam.us")));
            var project = ProjectLoader.Load(root);
            Assert.False(project.HasErrors, string.Join(Environment.NewLine, project.Diagnostics.Select(item => item.Message)));
            Assert.True(
                project.SourcePaths.Any(path => path.Replace('\\', '/').Contains("/paketler/SelamPaket/1.2.0/selam.us", StringComparison.OrdinalIgnoreCase)),
                $"sources={string.Join(" | ", project.SourcePaths)}; deps={string.Join(",", project.Dependencies.Select(d => d.Name + "@" + d.Version))}");
            var compiled = Compiler.Compile(project, new DotNetEmitBackend(), Path.Combine(root, "cikti"));
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
            var run = EmittedProcess.Run(compiled.ArtifactPath!);
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("paketten", run.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }

    [Fact]
    public void HashMismatchIsRejected()
    {
        var root = TempProject();
        var archive = Path.Combine(Path.GetTempPath(), "us-hash-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            BuildZip(archive, ("a.us", "fonksiyon A(): tamsayı => 1\n"));
            var installed = PackageCache.InstallFromArchive(root, archive, "A", "1.0.0", expectedSha256Hex: new string('0', 64));
            Assert.False(installed.Succeeded);
            Assert.Contains(installed.Diagnostics, item => item.Code == DiagnosticCodes.PackageIntegrity);

            var client = new PackageRegistryClient();
            var diagnostic = client.VerifyIntegrity(archive, new string('f', 64));
            Assert.NotNull(diagnostic);
            Assert.Equal(DiagnosticCodes.PackageIntegrity, diagnostic!.Code);
        }
        finally
        {
            Directory.Delete(root, true);
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }

    [Fact]
    public void MatchingHashAcceptsArchive()
    {
        var root = TempProject();
        var archive = Path.Combine(Path.GetTempPath(), "us-hash-ok-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            BuildZip(archive, ("a.us", "fonksiyon A(): tamsayı => 1\n"));
            var hash = PackageCache.ComputeSha256Hex(archive);
            var installed = PackageCache.InstallFromArchive(root, archive, "A", "1.0.0", expectedSha256Hex: hash);
            Assert.True(installed.Succeeded, string.Join(Environment.NewLine, installed.Diagnostics.Select(item => item.Message)));
            Assert.Null(new PackageRegistryClient().VerifyIntegrity(archive, hash));
        }
        finally
        {
            Directory.Delete(root, true);
            if (File.Exists(archive))
            {
                File.Delete(archive);
            }
        }
    }

    [Fact]
    public async Task OfflineResolveDoesNotRequireNetwork()
    {
        var client = new PackageRegistryClient(new Uri("https://example.invalid/us/"));
        Assert.Null(client.TryGetMetadata("X", "1.0.0"));
        var download = await client.DownloadAsync(
            new PackageRegistryMetadata("X", "1.0.0", "https://example.invalid/x.zip", null),
            Path.GetTempPath());
        Assert.Null(download.Path);
        Assert.Equal(DiagnosticCodes.PackageNotFound, download.Error!.Code);
    }

    [Fact]
    public void ProjectIsolationDoesNotMutateSiblingManifest()
    {
        var parent = Path.Combine(Path.GetTempPath(), "us-iso-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(parent, "A");
        var b = Path.Combine(parent, "B");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            WriteManifest(a, """
                proje "A"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """);
            WriteManifest(b, """
                proje "B"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """);
            File.WriteAllText(Path.Combine(a, "ana.us"), "syaz \"a\"\n");
            File.WriteAllText(Path.Combine(b, "ana.us"), "syaz \"b\"\n");
            var before = File.ReadAllText(Path.Combine(b, "us.proje"));
            InstallPackage(a, "Util", "1.0.0", ("u.us", "fonksiyon U(): tamsayı => 1\n"));
            File.AppendAllText(Path.Combine(a, "us.proje"), "paket \"Util\" sürüm \"1.0.0\"\n");
            _ = ProjectLoader.Load(a);
            Assert.Equal(before, File.ReadAllText(Path.Combine(b, "us.proje")));
            Assert.False(Directory.Exists(Path.Combine(b, "paketler")));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    private static string TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteManifest(string root, string text) =>
        File.WriteAllText(Path.Combine(root, "us.proje"), text.Replace("\r\n", "\n", StringComparison.Ordinal));

    private static void InstallPackage(string projectRoot, string name, string version, params (string Relative, string Content)[] files)
    {
        var dest = PackageResolver.PackageRoot(projectRoot, name, version);
        Directory.CreateDirectory(dest);
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(dest, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private static void BuildZip(string path, params (string Name, string Content)[] entries)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }
}
