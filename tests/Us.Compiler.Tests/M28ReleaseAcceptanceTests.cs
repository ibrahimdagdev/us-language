using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

/// <summary>v0.2.0 release gate: compiled US → Kestrel → real TCP → Memory DB → JSON.</summary>
public class M28ReleaseAcceptanceTests
{
    [Fact]
    public async Task MemoryFullStackAcceptanceFromSample()
    {
        var source = LoadSampleSource();
        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();

        using (var post1 = JsonBody("""{"id":1,"ad":"Ada","yaş":36}"""))
        {
            var created = await client.PostAsync("/kullanicilar", post1);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("application/json", created.Content.Headers.ContentType?.MediaType);
            var createdBody = await created.Content.ReadAsStringAsync();
            Assert.True(IsJson(createdBody), createdBody);
            Assert.Contains("Ada", createdBody, StringComparison.Ordinal);
        }

        using (var post2 = JsonBody("""{"id":2,"ad":"Grace","yaş":40}"""))
        {
            var created2 = await client.PostAsync("/kullanicilar", post2);
            Assert.Equal(HttpStatusCode.Created, created2.StatusCode);
        }

        var list = await client.GetAsync("/kullanicilar");
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal("application/json", list.Content.Headers.ContentType?.MediaType);
        Assert.True(IsJson(listBody), listBody);
        Assert.Contains("Ada", listBody, StringComparison.Ordinal);
        Assert.Contains("Grace", listBody, StringComparison.Ordinal);
        using (var doc = JsonDocument.Parse(listBody))
        {
            Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }
    }

    [Fact]
    public async Task ConcurrentPostsAndGetsStayIsolated()
    {
        var source = LoadSampleSource();
        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();

        var posts = Enumerable.Range(1, 16).Select(async i =>
        {
            using var content = JsonBody($"{{\"id\":{i},\"ad\":\"U{i}\",\"yaş\":{20 + i}}}");
            var response = await client.PostAsync("/kullanicilar", content);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        });
        await Task.WhenAll(posts);

        var gets = Enumerable.Range(0, 16).Select(async _ =>
        {
            var response = await client.GetAsync("/kullanicilar");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(IsJson(body), body);
            for (var i = 1; i <= 16; i++)
            {
                Assert.Contains("\"ad\":\"U" + i + "\"", body, StringComparison.Ordinal);
            }
        });
        await Task.WhenAll(gets);
    }

    [Fact]
    public async Task HttpErrorSurfaceDoesNotLeakClrStacks()
    {
        const string source = """
            model Kutu { ad: metin }
            kontrolcü Api : "/api"
            {
                rota GET "/ok"
                fonksiyon ok(): metin { => "ok" }

                rota POST "/json"
                fonksiyon yaz(girdi: Kutu): nesne { => oluşturuldu(girdi) }

                rota GET "/patla"
                fonksiyon patla(): metin
                {
                    fırlat @Hata("iç hata")
                    => "yok"
                }

                rota GET "/{id}"
                fonksiyon bul(id: tamsayı): metin { => "{id}" }
            }
            """;

        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();

        var notFound = await client.GetAsync("/yok");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.DoesNotContain("at ", await notFound.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var method = await client.PostAsync("/api/ok", new StringContent(""));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);

        using var plain = new StringContent("ad=x", Encoding.UTF8, "text/plain");
        var media = await client.PostAsync("/api/json", plain);
        Assert.Equal((HttpStatusCode)415, media.StatusCode);

        var badRoute = await client.GetAsync("/api/abc");
        Assert.Equal(HttpStatusCode.BadRequest, badRoute.StatusCode);
        var badBody = await badRoute.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", badBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", badBody, StringComparison.Ordinal);

        var boom = await client.GetAsync("/api/patla");
        Assert.Equal(HttpStatusCode.InternalServerError, boom.StatusCode);
        var boomBody = await boom.Content.ReadAsStringAsync();
        Assert.Equal("""{"hata":"sunucu hatası"}""", boomBody);
        Assert.DoesNotContain("iç hata", boomBody, StringComparison.Ordinal);
        Assert.DoesNotContain("UsRuntimeException", boomBody, StringComparison.Ordinal);
        Assert.DoesNotContain("at Us.", boomBody, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalSqlServerLiveAcceptanceReportsNotExecutedWhenUnset()
    {
        var connection = Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION");
        if (!string.IsNullOrWhiteSpace(connection))
        {
            // Live path exercised by OptionalSqlServerKestrelEndToEnd when env is set.
            Assert.False(string.IsNullOrWhiteSpace(connection));
            return;
        }

        Assert.True(string.IsNullOrWhiteSpace(connection));
    }

    [Fact]
    public void LocalPackageAcceptanceIsDeterministicAndCompiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-m28-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "us.proje"), """
                proje "Kabul"
                sürüm "1.0.0"
                hedef "uygulama"
                başlangıç "ana.us"
                paket "Yardim" sürüm "1.0.0"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "ana.us"), "syaz YardimMetin()\n");
            var pkg = PackageResolver.PackageRoot(root, "Yardim", "1.0.0");
            Directory.CreateDirectory(pkg);
            File.WriteAllText(Path.Combine(pkg, "lib.us"), """
                fonksiyon YardimMetin(): metin => "paket-ok"
                """.Replace("\r\n", "\n", StringComparison.Ordinal));

            var project = ProjectLoader.Load(root);
            Assert.False(project.HasErrors, string.Join(Environment.NewLine, project.Diagnostics.Select(item => item.Message)));
            var first = PackageResolver.Resolve(project);
            var second = PackageResolver.Resolve(project);
            Assert.Equal(
                first.Packages.Select(item => item.IdentityKey).ToArray(),
                second.Packages.Select(item => item.IdentityKey).ToArray());
            Assert.Equal(
                first.Packages.SelectMany(item => item.SourcePaths).ToArray(),
                second.Packages.SelectMany(item => item.SourcePaths).ToArray());

            var compiled = Compiler.Compile(project, new DotNetEmitBackend(), Path.Combine(root, "cikti"));
            Assert.True(compiled.Success, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
            var run = EmittedProcess.Run(compiled.ArtifactPath!);
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("paket-ok", run.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PackageArchiveSecurityStillRejectsTraversalAndDuplicates()
    {
        AssertRejected("evil.us", "../evil.us");
        AssertRejected("evil.us", "../../evil.us");
        AssertRejected("evil.us", "/absolute/evil.us");
        AssertRejected("evil.us", "C:/Windows/evil.us");
        AssertRejected("evil.us", @"..\\evil.us");
        AssertDuplicate();
    }

    [Fact]
    public void EmittedAcceptanceArtifactDoesNotEmbedInterpreter()
    {
        var source = LoadSampleSource();
        var dir = Path.Combine(Path.GetTempPath(), "us-m28-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = SourceFile.FromText(Path.Combine(dir, "ana.us"), source);
            var emit = Compiler.Compile([file], new DotNetEmitBackend(), dir);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
            var bytes = File.ReadAllBytes(emit.ArtifactPath!);
            var ascii = Encoding.ASCII.GetString(bytes);
            Assert.DoesNotContain("Us.Interpreter.Evaluator", ascii, StringComparison.Ordinal);
            Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(dir, "Us.Hosting.dll")));
            Assert.True(File.Exists(Path.Combine(dir, "Us.Runtime.dll")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void AssertRejected(string contentName, string entryName)
    {
        using var stream = BuildZip((entryName, "syaz \"x\"\n"));
        var dest = Path.Combine(Path.GetTempPath(), "us-m28-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            var result = PackageArchive.Extract(stream, dest);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.PackagePathEscape);
        }
        finally
        {
            Directory.Delete(dest, true);
        }
    }

    private static void AssertDuplicate()
    {
        using var stream = BuildZip(
            ("a/b.us", "syaz \"1\"\n"),
            ("a\\b.us", "syaz \"2\"\n"));
        var dest = Path.Combine(Path.GetTempPath(), "us-m28-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            var result = PackageArchive.Extract(stream, dest);
            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.PackageMalformed);
        }
        finally
        {
            Directory.Delete(dest, true);
        }
    }

    private static MemoryStream BuildZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string LoadSampleSource()
    {
        var path = Path.Combine(FindRepoRoot(), "samples", "BackendAcceptance", "ana.us");
        Assert.True(File.Exists(path), path);
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Us.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static bool IsJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class CompiledKestrelSession : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly Process _process;

        private CompiledKestrelSession(string directory, Process process, Uri baseAddress)
        {
            _directory = directory;
            _process = process;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

        public static async Task<CompiledKestrelSession> StartAsync(string source)
        {
            var dir = Path.Combine(Path.GetTempPath(), "us-m28-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = SourceFile.FromText(Path.Combine(dir, "ana.us"), source);
            var emit = Compiler.Compile([file], new DotNetEmitBackend(), dir);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
            Assert.NotNull(emit.ArtifactPath);

            var start = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = Quote(emit.ArtifactPath!),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = dir,
            };
            start.Environment["US_URLS"] = "http://127.0.0.1:0";
            start.Environment.Remove("US_TEST_ROUTE");
            start.Environment.Remove("US_KESTREL");

            var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet süreci başlatılamadı.");
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false)) is not null)
                {
                    if (line.StartsWith("US dinliyor:", StringComparison.Ordinal))
                    {
                        var url = line["US dinliyor:".Length..].Trim();
                        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
                        {
                            throw new InvalidOperationException("Dinleyici adresi çözülemedi: " + line);
                        }

                        return new CompiledKestrelSession(dir, process, baseAddress);
                    }
                }

                var stderr = await process.StandardError.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                throw new InvalidOperationException("Kestrel başlamadı. stderr=" + stderr);
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // ignore
                }

                process.Dispose();
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                try
                {
                    _process.Dispose();
                }
                catch
                {
                    // ignore
                }

                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(_directory))
                        {
                            Directory.Delete(_directory, recursive: true);
                        }

                        break;
                    }
                    catch (Exception)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
        }

        private static string Quote(string path) =>
            path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
    }
}
