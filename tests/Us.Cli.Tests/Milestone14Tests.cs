using Us.Cli;
using Us.Core;

namespace Us.Cli.Tests;

public class CliRouterTests
{
    [Fact]
    public async Task UnknownCommandIsUS1702()
    {
        var host = M14Helpers.TempHost();
        var result = await UsCli.RunAsync(["bilinmeyen"], host);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.UnknownCommand);
        Assert.Contains("Kullanım", host.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CliDoesNotReferenceSemanticsTypes()
    {
        var assembly = typeof(UsCli).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), item => item.Name == "Us.Semantics");
        var sources = Directory.EnumerateFiles(M14Helpers.FindSrc("Us.Cli"), "*.cs");
        foreach (var path in sources)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Us.Semantics", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Evaluator", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Binder", text, StringComparison.Ordinal);
        }
    }
}

public class CliRunTests
{
    [Fact]
    public async Task RunMerhabaPrintsExpectedLine()
    {
        var sample = Path.Combine(M14Helpers.FindRepo(), "samples", "Merhaba", "ana.us");
        var host = new CliHost(Path.GetTempPath());
        var result = await UsCli.RunAsync(["çalıştır", sample], host);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Output, line => line.Contains("Merhaba İbrahim", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BadFileExitsOne()
    {
        var root = M14Helpers.TempDir();
        try
        {
            var path = Path.Combine(root, "kirik.us");
            File.WriteAllText(path, "bu : tamsayı = \"x\"\n");
            var host = new CliHost(root);
            var result = await UsCli.RunAsync(["çalıştır", path], host);
            Assert.Equal(1, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class CliNewTests
{
    [Fact]
    public async Task ThreeTemplatesProduceExpectedFiles()
    {
        var root = M14Helpers.TempDir();
        try
        {
            var host = new CliHost(root);
            Assert.Equal(0, (await UsCli.RunAsync(["yeni", "konsol", "Merhaba"], host)).ExitCode);
            Assert.True(File.Exists(Path.Combine(root, "Merhaba", "us.proje")));
            Assert.True(File.Exists(Path.Combine(root, "Merhaba", "ana.us")));

            Assert.Equal(0, (await UsCli.RunAsync(["yeni", "kütüphane", "Matematik"], host)).ExitCode);
            Assert.True(File.Exists(Path.Combine(root, "Matematik", "us.proje")));
            Assert.False(File.Exists(Path.Combine(root, "Matematik", "ana.us")));

            Assert.Equal(0, (await UsCli.RunAsync(["yeni", "web", "TicaretApi"], host)).ExitCode);
            var web = await File.ReadAllTextAsync(Path.Combine(root, "TicaretApi", "ana.us"));
            Assert.Contains("kontrolcü", web, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExistingFilesAreUS1703()
    {
        var root = M14Helpers.TempDir();
        try
        {
            var host = new CliHost(root);
            Directory.CreateDirectory(Path.Combine(root, "X"));
            File.WriteAllText(Path.Combine(root, "X", "us.proje"), "proje \"X\"\n");
            var result = await UsCli.RunAsync(["yeni", "konsol", "X"], host);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.ExistingPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class CliBuildTests
{
    [Fact]
    public async Task SolidProjectExitsZero()
    {
        var root = M14Helpers.TempDir();
        try
        {
            var host = new CliHost(root);
            await UsCli.RunAsync(["yeni", "konsol", "Derle"], host);
            var projectHost = new CliHost(Path.Combine(root, "Derle"));
            var result = await UsCli.RunAsync(["derle"], projectHost);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains(result.Output, line => line.Contains("derlemesi hazır", StringComparison.OrdinalIgnoreCase) || line.Contains("derleme hazır", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class CliTestCommandTests
{
    [Fact]
    public async Task SamplesTestlerExitsZero()
    {
        var host = new CliHost(Path.Combine(M14Helpers.FindRepo(), "samples", "Testler"));
        var result = await UsCli.RunAsync(["test"], host);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task FailingTestExitsOne()
    {
        var root = M14Helpers.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "us.proje"), """
                proje "Kalan"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                test "kasıtlı kalan"
                {
                    beklenen 1 == 2
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            var result = await UsCli.RunAsync(["test"], new CliHost(root));
            Assert.Equal(1, result.ExitCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class CliPublishTests
{
    [Fact]
    public async Task PublishContainsVersionAndRejectsExisting()
    {
        var root = M14Helpers.TempDir();
        try
        {
            var host = new CliHost(root);
            await UsCli.RunAsync(["yeni", "konsol", "Yayin"], host);
            var project = Path.Combine(root, "Yayin");
            var first = await UsCli.RunAsync(["yayınla"], new CliHost(project));
            Assert.Equal(0, first.ExitCode);
            var summary = await File.ReadAllTextAsync(Path.Combine(project, "yayin", "ozet.txt"));
            Assert.Contains("1.0.0", summary, StringComparison.Ordinal);
            var second = await UsCli.RunAsync(["yayınla"], new CliHost(project));
            Assert.Equal(1, second.ExitCode);
            Assert.Contains(second.Diagnostics, item => item.Code == DiagnosticCodes.ExistingPublish);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class PackageAddTests
{
    [Fact]
    public async Task PackageLineIsAddedAndAlDoesNotDownload()
    {
        var root = M14Helpers.TempDir();
        try
        {
            await UsCli.RunAsync(["yeni", "konsol", "Paket"], new CliHost(root));
            var project = Path.Combine(root, "Paket");
            var result = await UsCli.RunAsync(["paket", "ekle", "US.Json"], new CliHost(project));
            Assert.Equal(0, result.ExitCode);
            var manifest = await File.ReadAllTextAsync(Path.Combine(project, "us.proje"));
            Assert.Contains("paket \"US.Json\"", manifest, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(project, "ana.us"), "al US.Json\nsyaz \"ok\"\n");
            await UsCli.RunAsync(["çalıştır"], new CliHost(project));
            Assert.False(Directory.Exists(Path.Combine(project, "paketler")));
            Assert.False(Directory.Exists(Path.Combine(project, "packages")));
            Assert.DoesNotContain(Directory.EnumerateDirectories(project), path =>
                Path.GetFileName(path).Contains("paket", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PackageAddIsIdempotentForExactVersion()
    {
        var root = M14Helpers.TempDir();
        try
        {
            await UsCli.RunAsync(["yeni", "konsol", "Paket"], new CliHost(root));
            var project = Path.Combine(root, "Paket");
            Assert.Equal(0, (await UsCli.RunAsync(["paket", "ekle", "Util", "1.0.0"], new CliHost(project))).ExitCode);
            var first = await File.ReadAllTextAsync(Path.Combine(project, "us.proje"));
            var second = await UsCli.RunAsync(["paket", "ekle", "Util", "1.0.0"], new CliHost(project));
            Assert.Equal(0, second.ExitCode);
            Assert.Equal(first, await File.ReadAllTextAsync(Path.Combine(project, "us.proje")));
            Assert.Contains("zaten kayıtlı", string.Join('\n', second.Output), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PackageInstallFromArchiveUpdatesCacheAndManifest()
    {
        var root = M14Helpers.TempDir();
        var archive = Path.Combine(root, "util.zip");
        try
        {
            await UsCli.RunAsync(["yeni", "konsol", "Paket"], new CliHost(root));
            var project = Path.Combine(root, "Paket");
            using (var stream = File.Create(archive))
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("us.proje");
                await using (var writer = new StreamWriter(entry.Open()))
                {
                    await writer.WriteAsync("""
                        proje "Util"
                        sürüm "1.0.0"
                        hedef "kütüphane"
                        """);
                }

                var source = zip.CreateEntry("util.us");
                await using (var writer = new StreamWriter(source.Open()))
                {
                    await writer.WriteAsync("fonksiyon UtilMetin(): metin => \"u\"\n");
                }
            }

            var result = await UsCli.RunAsync(["paket", "kur", archive], new CliHost(project));
            Assert.Equal(0, result.ExitCode);
            Assert.True(Directory.Exists(Path.Combine(project, "paketler", "Util", "1.0.0")));
            var manifest = await File.ReadAllTextAsync(Path.Combine(project, "us.proje"));
            Assert.Contains("paket \"Util\" sürüm \"1.0.0\"", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class CliDiagnosticTests
{
    [Fact]
    public async Task MissingEntryMessageIsTurkishUS1701()
    {
        var root = M14Helpers.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "us.proje"), """
                proje "X"
                sürüm "1.0.0"
                hedef "uygulama"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            var result = await UsCli.RunAsync(["çalıştır"], new CliHost(root));
            Assert.Equal(1, result.ExitCode);
            var diagnostic = Assert.Single(result.Diagnostics, item => item.Code == DiagnosticCodes.MissingEntry);
            Assert.Contains("Giriş", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SyncWithoutApprovalReportsDestructive()
    {
        var root = M14Helpers.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "us.proje"), """
                proje "Db"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                    ad: metin
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            Assert.Equal(0, (await UsCli.RunAsync(["veritabanı", "senkronize"], new CliHost(root))).ExitCode);
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            var result = await UsCli.RunAsync(["veritabanı", "senkronize"], new CliHost(root));
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.DestructiveSchema);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class MultiFileProjectTests
{
    [Fact]
    public async Task MultiFileProjectCompilesAndRuns()
    {
        var root = M14Helpers.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "us.proje"), """
                proje "Coklu"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "Modeller.us"), """
                fonksiyon selam(ad: metin): metin
                {
                    => "Merhaba {ad}"
                }
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                syaz selam("İbrahim")
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            var result = await UsCli.RunAsync(["çalıştır"], new CliHost(root));
            Assert.Equal(0, result.ExitCode);
            Assert.Contains(result.Output, line => line.Contains("Merhaba İbrahim", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class ParityTests
{
    [Fact]
    public void InterpreterAndEmittedBackendMatchForMerhabaSubset()
    {
        var source = """
            ad: metin = "İbrahim"
            syaz "Merhaba {ad}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var interpreted = Us.Interpreter.ProgramRunner.RunText("p.us", source);
        Assert.True(interpreted.Evaluated);
        var expected = string.Join('\n', interpreted.Output.Lines) + "\n";

        var bound = Us.Compiler.Compiler.BindOnly([Us.Core.SourceFile.FromText("p.us", source)]);
        var dir = Path.Combine(Path.GetTempPath(), "us-parity-" + Guid.NewGuid().ToString("N"));
        var emit = new Us.Compiler.DotNetEmitBackend().Emit(bound, dir);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
        var process = Us.Compiler.EmittedProcess.Run(emit.ArtifactPath!);
        Assert.Equal(0, process.ExitCode);
        var actual = process.StandardOutput.EndsWith('\n') ? process.StandardOutput : process.StandardOutput + "\n";
        Assert.Equal(expected, actual);
    }
}

internal static class M14Helpers
{
    public static CliHost TempHost() => new(TempDir());

    public static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "us-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    public static string FindSrc(string name) => Path.Combine(FindRepo(), "src", name);
}
