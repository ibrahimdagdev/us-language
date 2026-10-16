using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Us.Compiler;
using Us.Core;
using Us.Runtime;

namespace Us.Compiler.Tests;

public class KestrelParityTests
{
    [Fact]
    public async Task CompiledUsSourceServesGetOverRealKestrel()
    {
        const string source = """
            kontrolcü Merhaba : "/api"
            {
                rota GET "/selam"
                fonksiyon selam(): metin
                {
                    => "merhaba"
                }
            }
            """;

        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();
        var response = await client.GetAsync("/api/selam");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("merhaba", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CompiledMemoryDatabaseJsonRoundTripOverKestrel()
    {
        const string source = """
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            veritabanı Db
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: Db
            kontrolcü Api : "/kullanicilar"
            {
                rota POST "/"
                afonksiyon ekle(girdi: Kullanıcı): nesne
                {
                    bekle db.kullanıcılar.ekle(girdi)
                    => oluşturuldu(girdi)
                }

                rota GET "/"
                afonksiyon tumunuGetir(): nesne
                {
                    satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
                    => tamam(satırlar)
                }
            }
            """;

        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();

        using var post = new StringContent("""{"id":1,"ad":"Ada"}""", Encoding.UTF8, "application/json");
        var created = await client.PostAsync("/kullanicilar", post);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("Ada", await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var got = await client.GetAsync("/kullanicilar");
        var gotBody = await got.Content.ReadAsStringAsync();
        Assert.True(got.StatusCode == HttpStatusCode.OK, "GET status=" + (int)got.StatusCode + " body=" + gotBody);
        Assert.Contains("Ada", gotBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompiledUnsupportedMediaIs415()
    {
        const string source = """
            model Kişi { ad: metin }
            kontrolcü X : "/x"
            {
                rota POST "/"
                fonksiyon yaz(girdi: Kişi): nesne
                {
                    => oluşturuldu(girdi)
                }
            }
            """;

        await using var session = await CompiledKestrelSession.StartAsync(source);
        using var client = session.CreateClient();
        using var plain = new StringContent("ad=Ada", Encoding.UTF8, "text/plain");
        var rejected = await client.PostAsync("/x", plain);
        Assert.Equal((HttpStatusCode)415, rejected.StatusCode);
    }

    [Fact]
    public void UsTestRouteStillWorksWithoutKestrelListen()
    {
        const string source = """
            kontrolcü Merhaba : "/api"
            {
                rota GET "/selam"
                fonksiyon selam(): metin
                {
                    => "merhaba"
                }
            }
            """;

        var dir = Path.Combine(Path.GetTempPath(), "us-kestrel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = SourceFile.FromText(Path.Combine(dir, "ana.us"), source.Replace("\r\n", "\n", StringComparison.Ordinal));
            var emit = Compiler.Compile([file], new DotNetEmitBackend(), dir);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
            var run = EmittedProcess.Run(
                emit.ArtifactPath!,
                environment: new Dictionary<string, string>
                {
                    ["US_TEST_ROUTE"] = "GET /api/selam",
                    ["US_KESTREL"] = "0",
                });
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("200", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("merhaba", run.StandardOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("US dinliyor", run.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OptionalSqlServerKestrelEndToEnd()
    {
        var connection = Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            return;
        }

        using (var wipe = new Microsoft.Data.SqlClient.SqlConnection(connection))
        {
            wipe.Open();
            using var command = wipe.CreateCommand();
            command.CommandText = """
                IF OBJECT_ID(N'[siparişler]', N'U') IS NOT NULL DROP TABLE [siparişler];
                IF OBJECT_ID(N'[kullanıcılar]', N'U') IS NOT NULL DROP TABLE [kullanıcılar];
                CREATE TABLE [kullanıcılar] (
                    [id] int NOT NULL,
                    [ad] nvarchar(max) NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        const string source = """
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            veritabanı Db
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: Db
            kontrolcü Api : "/kullanicilar"
            {
                rota POST "/"
                afonksiyon ekle(girdi: Kullanıcı): nesne
                {
                    bekle db.kullanıcılar.ekle(girdi)
                    => oluşturuldu(girdi)
                }

                rota GET "/"
                afonksiyon tumunuGetir(): nesne
                {
                    satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
                    => tamam(satırlar)
                }
            }
            """;

        await using var session = await CompiledKestrelSession.StartAsync(
            source,
            extraEnvironment: new Dictionary<string, string>
            {
                ["US_TEST_SQLSERVER_CONNECTION"] = connection,
            });
        using var client = session.CreateClient();

        using var post1 = new StringContent("""{"id":9001,"ad":"SqlAda"}""", Encoding.UTF8, "application/json");
        var created1 = await client.PostAsync("/kullanicilar", post1);
        Assert.True(created1.StatusCode == HttpStatusCode.Created, "POST1=" + (int)created1.StatusCode + " " + await created1.Content.ReadAsStringAsync());

        using var post2 = new StringContent("""{"id":9002,"ad":"O'Brien"}""", Encoding.UTF8, "application/json");
        var created2 = await client.PostAsync("/kullanicilar", post2);
        Assert.True(created2.StatusCode == HttpStatusCode.Created, "POST2=" + (int)created2.StatusCode + " " + await created2.Content.ReadAsStringAsync());

        var list = await client.GetAsync("/kullanicilar");
        var body = await list.Content.ReadAsStringAsync();
        Assert.True(
            list.StatusCode == HttpStatusCode.OK,
            "GET status=" + (int)list.StatusCode + " body=" + body + " stderr=" + session.Stderr);
        Assert.Contains("SqlAda", body, StringComparison.Ordinal);
        Assert.Contains("O'Brien", body, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 2, body);
    }

    private sealed class CompiledKestrelSession : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly Process _process;

        private readonly StringBuilder _stderr = new();

        private CompiledKestrelSession(string directory, Process process, Uri baseAddress)
        {
            _directory = directory;
            _process = process;
            BaseAddress = baseAddress;
            _ = DrainStderrAsync();
        }

        public Uri BaseAddress { get; }

        public string Stderr
        {
            get
            {
                lock (_stderr)
                {
                    return _stderr.ToString();
                }
            }
        }

        public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

        private async Task DrainStderrAsync()
        {
            try
            {
                while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    lock (_stderr)
                    {
                        _stderr.AppendLine(line);
                    }
                }
            }
            catch
            {
                // process exited
            }
        }

        public static async Task<CompiledKestrelSession> StartAsync(
            string source,
            IReadOnlyDictionary<string, string>? extraEnvironment = null)
        {
            var dir = Path.Combine(Path.GetTempPath(), "us-kestrel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var file = SourceFile.FromText(Path.Combine(dir, "ana.us"), source.Replace("\r\n", "\n", StringComparison.Ordinal));
            var emit = Compiler.Compile([file], new DotNetEmitBackend(), dir);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(item => item.Message)));
            Assert.NotNull(emit.ArtifactPath);
            Assert.True(File.Exists(Path.Combine(dir, "Us.Hosting.dll")), "Us.Hosting.dll must be copied beside emitted program.");

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
            if (extraEnvironment is not null)
            {
                foreach (var pair in extraEnvironment)
                {
                    start.Environment[pair.Key] = pair.Value;
                }
            }

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
