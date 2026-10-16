using Us.Core;
using Us.Interpreter;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

file static class Web
{
    public const string Api = """
        model Kullanıcı
        {
            id: tamsayı : anahtar
            ad: metin
        }
        servis KullanıcıServisi : tekil
        {
            genel kayıtlar: liste<Kullanıcı> = []
            genel afonksiyon bul(id: tamsayı): Kullanıcı?
            {
                => bu.kayıtlar.filtrele(^.id == id).ilkYadaBoş()
            }
            genel afonksiyon ekle(kullanıcı: Kullanıcı): Kullanıcı
            {
                sonraki: tamsayı = bu.kayıtlar.adet + 1
                kullanıcı.id = sonraki
                bu.kayıtlar.ekle(kullanıcı)
                => kullanıcı
            }
        }
        kontrolcü KullanıcıKontrolcü : "/kullanicilar"
        {
            bağla servis: KullanıcıServisi
            rota GET "/{id}"
            afonksiyon bul(id: tamsayı): Kullanıcı?
            {
                => bekle servis.bul(id)
            }
            rota POST "/"
            afonksiyon ekle(kullanıcı: Kullanıcı): Kullanıcı
            {
                => bekle servis.ekle(kullanıcı)
            }
        }
        """;

    public static UsWebHost Host(string text, UsHost? host = null) => UsWebHost.FromText("web.us", text, host);

    public static BoundCompilation Compile(string text)
    {
        var file = SourceFile.FromText("web.us", text.Replace("\r\n", "\n", StringComparison.Ordinal));
        var compiled = UsCompile.Front(file);
        Assert.True(compiled.Errors.Length == 0, string.Join(Environment.NewLine, compiled.Errors.Select(item => item.Code + " " + item.Message)));
        return compiled.Compilation;
    }
}

public class RouteTableTests
{
    [Fact]
    public void GetRouteIsRegisteredWithCombinedPath()
    {
        var compilation = Web.Compile(Web.Api);
        var route = Assert.Single(compilation.Routes, item => item.Method == "GET");
        Assert.Equal("/kullanicilar/{id}", route.Template);
        Assert.Equal("bul", route.Handler.Name);
        Assert.Contains("id", route.Parameters);
        Assert.Contains(compilation.Controllers[0].Setup, item => item.Constant == "servis");
        Assert.True(compilation.Controllers[0].Setup[0].Type is SourceTypeSymbol, compilation.Controllers[0].Setup[0].Type.GetType().Name);
    }

    [Fact]
    public void UnknownVerbIsUs0866()
    {
        var file = SourceFile.FromText("bad.us", """
            kontrolcü X : "/x"
            {
                rota HEAD "/"
                fonksiyon a()
                {
                }
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        var compiled = UsCompile.Front(file);
        Assert.Contains(compiled.Errors, item => item.Code == DiagnosticCodes.UnknownHttpVerb && item.Message.Contains("HEAD", StringComparison.Ordinal) && item.Span.Length > 0);
    }
}

public class GetRouteTests
{
    [Fact]
    public async Task GetReturnsTheUser()
    {
        var host = Web.Host(Web.Api);
        var first = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", """{"id":0,"ad":"Ali"}""", "application/json"));
        Assert.Equal(200, first.StatusCode);
        var response = await host.DispatchAsync(new UsHttpRequest("GET", "/kullanicilar/1"));
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("Ali", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonIntegerIdIsBindingError()
    {
        var host = Web.Host(Web.Api);
        var response = await host.DispatchAsync(new UsHttpRequest("GET", "/kullanicilar/abc"));
        Assert.Equal(400, response.StatusCode);
    }
}

public class PostBodyTests
{
    [Fact]
    public async Task ValidBodyAddsUser()
    {
        var host = Web.Host(Web.Api);
        var response = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", """{"id":0,"ad":"Ali"}""", "application/json"));
        Assert.Equal(200, response.StatusCode);
        Assert.Contains("Ali", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownFieldIsRejected()
    {
        var host = Web.Host(Web.Api);
        var response = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", """{"id":0,"ad":"Ali","fazla":1}""", "application/json"));
        Assert.Equal(400, response.StatusCode);
    }
}

public class HttpResultTests
{
    [Fact]
    public void EightHelpersReturnExpectedCodes()
    {
        var result = Run.Clean("""
            tamam("x")
            oluşturuldu("x")
            içerikYok()
            hatalıİstek("x")
            yetkisiz("x")
            yasak("x")
            bulunamadı("x")
            çakışma("x")
            syaz "ok"
            """);
        Assert.True(result.Evaluated, string.Join("|", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["ok"], result.Output.Lines);
    }

    [Fact]
    public async Task HelpersShapeHttpResponses()
    {
        var host = Web.Host("""
            kontrolcü X : "/x"
            {
                rota GET "/a"
                fonksiyon a(): nesne { => tamam("tam") }
                rota GET "/b"
                fonksiyon b(): nesne { => oluşturuldu("yeni") }
                rota GET "/c"
                fonksiyon c(): nesne { => içerikYok() }
                rota GET "/d"
                fonksiyon d(): nesne { => hatalıİstek("kötü") }
                rota GET "/e"
                fonksiyon e(): nesne { => yetkisiz("yok") }
                rota GET "/f"
                fonksiyon f(): nesne { => yasak("yasak") }
                rota GET "/g"
                fonksiyon g(): nesne { => bulunamadı("yok") }
                rota GET "/h"
                fonksiyon h(): nesne { => çakışma("var") }
            }
            """);
        Assert.Equal(200, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/a"))).StatusCode);
        Assert.Equal(201, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/b"))).StatusCode);
        Assert.Equal(204, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/c"))).StatusCode);
        Assert.Equal(400, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/d"))).StatusCode);
        Assert.Equal(401, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/e"))).StatusCode);
        Assert.Equal(403, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/f"))).StatusCode);
        Assert.Equal(404, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/g"))).StatusCode);
        Assert.Equal(409, (await host.DispatchAsync(new UsHttpRequest("GET", "/x/h"))).StatusCode);
    }
}

public class FakeTransportTests
{
    [Fact]
    public void RegisteredResponseReturnsBody()
    {
        var transport = new FakeHttpTransport();
        transport.Register("GET", "https://ornek/a", new UsHttpClientResponse(200, "{\"ad\":\"Ali\"}", "application/json", new Dictionary<string, string>()));
        var host = new UsHost { Transport = transport };
        var result = ProgramRunner.RunText("t.us", """
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcı: Kullanıcı = bekle http.GET("https://ornek/a").json()
            syaz kullanıcı.ad
            """, host);
        Assert.True(result.Evaluated, string.Join(",", result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(["Ali"], result.Output.Lines);
    }

    [Fact]
    public void MissingPathIsUs1534()
    {
        var host = new UsHost { Transport = new FakeHttpTransport() };
        var result = ProgramRunner.RunText("t.us", """
            bekle http.GET("https://yok")
            """, host);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.UnknownTransportPath);
    }
}

public class HttpVerbTests
{
    [Fact]
    public void FiveVerbsReachTransport()
    {
        var transport = new RecordingTransport();
        var host = new UsHost { Transport = transport };
        ProgramRunner.RunText("t.us", """
            bekle http.GET("u1")
            bekle http.POST("u2", "x")
            bekle http.PUT("u3", "x")
            bekle http.PATCH("u4", "x")
            bekle http.DELETE("u5")
            """, host);
        Assert.Equal(["GET", "POST", "PUT", "PATCH", "DELETE"], transport.Methods);
    }

    private sealed class RecordingTransport : IHttpTransport
    {
        public List<string> Methods { get; } = [];

        public ValueTask<UsHttpClientResponse> SendAsync(string method, string url, string? body, string? contentType, IReadOnlyDictionary<string, string> headers)
        {
            Methods.Add(method);
            return ValueTask.FromResult(new UsHttpClientResponse(200, "{}", "application/json", new Dictionary<string, string>()));
        }
    }
}

public class HeaderTests
{
    [Fact]
    public void AuthorizationHeaderIsSent()
    {
        var transport = new HeaderTransport();
        var host = new UsHost { Transport = transport };
        ProgramRunner.RunText("t.us", """
            bekle http.başlık("Authorization", "Bearer t").GET("u")
            """, host);
        Assert.Equal("Bearer t", transport.Headers["Authorization"]);
    }

    [Fact]
    public void EmptyHeaderNameIsUs0867()
    {
        var host = new UsHost { Transport = new FakeHttpTransport() };
        var result = ProgramRunner.RunText("t.us", """
            bekle http.başlık("", "x").GET("u")
            """, host);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.EmptyHeaderName);
    }

    private sealed class HeaderTransport : IHttpTransport
    {
        public Dictionary<string, string> Headers { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<UsHttpClientResponse> SendAsync(string method, string url, string? body, string? contentType, IReadOnlyDictionary<string, string> headers)
        {
            Headers = headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return ValueTask.FromResult(new UsHttpClientResponse(200, "{}", "application/json", Headers));
        }
    }
}

public class HttpJsonTests
{
    [Fact]
    public void TypedJsonFillsFields()
    {
        var transport = new FakeHttpTransport();
        transport.Register("GET", "u", new UsHttpClientResponse(200, "{\"ad\":\"Ali\"}", "application/json", new Dictionary<string, string>()));
        var host = new UsHost { Transport = transport };
        var result = ProgramRunner.RunText("t.us", """
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcı: Kullanıcı = bekle http.GET("u").json()
            syaz kullanıcı.ad
            """, host);
        Assert.Equal(["Ali"], result.Output.Lines);
    }

    [Fact]
    public void UntypedJsonIsUs0855()
    {
        var file = SourceFile.FromText("t.us", """
            bekle http.GET("u").json()
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        var compiled = UsCompile.Front(file);
        Assert.Contains(compiled.Errors, item => item.Code == DiagnosticCodes.JsonTarget);
    }
}

public class WebApiSampleTests
{
    [Fact]
    public async Task SampleGetAndPostWorkOnFakeHost()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "samples", "WebApi", "ana.us");
        Assert.True(File.Exists(path), path);
        var host = UsWebHost.FromText(path, File.ReadAllText(path));
        var post = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", """{"id":0,"ad":"Ali"}""", "application/json"));
        Assert.Equal(200, post.StatusCode);
        var get = await host.DispatchAsync(new UsHttpRequest("GET", "/kullanicilar/1"));
        Assert.Equal(200, get.StatusCode);
        Assert.Contains("Ali", get.Body, StringComparison.Ordinal);
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

public class WebDiagnosticTests
{
    [Fact]
    public async Task VerbAndBindingDiagnosticsAreTurkishWithSpan()
    {
        var file = SourceFile.FromText("d.us", """
            kontrolcü X : "/x"
            {
                rota HEAD "/"
                fonksiyon a()
                {
                }
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal));
        var compiled = UsCompile.Front(file);
        var verb = Assert.Single(compiled.Errors, item => item.Code == DiagnosticCodes.UnknownHttpVerb);
        Assert.Contains("fiil", verb.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(verb.Span.Length > 0);

        var host = Web.Host(Web.Api);
        var bad = await host.DispatchAsync(new UsHttpRequest("GET", "/kullanicilar/abc"));
        Assert.Equal(400, bad.StatusCode);
        Assert.Contains("dönüştürülemedi", bad.Body, StringComparison.Ordinal);

        var media = await host.DispatchAsync(new UsHttpRequest("POST", "/kullanicilar", "ad=Ali", "text/plain"));
        Assert.Equal(415, media.StatusCode);
        Assert.Contains("application/json", media.Body, StringComparison.Ordinal);
    }
}

public class WebScopeAndDatabaseTests
{
    [Fact]
    public async Task ScopedServiceDiffersAcrossRequestsAndDatabasePersists()
    {
        var host = Web.Host("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            kontrolcü X : "/x"
            {
                bağla db: UygulamaDb
                rota POST "/kullanici"
                afonksiyon ekle(kullanıcı: Kullanıcı): Kullanıcı
                {
                    bekle db.kullanıcılar.ekle(kullanıcı)
                    => kullanıcı
                }
                rota GET "/adet"
                afonksiyon adet(): tamsayı
                {
                    satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
                    => satırlar.adet
                }
            }
            """);
        await host.DispatchAsync(new UsHttpRequest("POST", "/x/kullanici", """{"id":0,"ad":"Ali"}""", "application/json"));
        var count = await host.DispatchAsync(new UsHttpRequest("GET", "/x/adet"));
        Assert.Equal(200, count.StatusCode);
        Assert.Contains("1", count.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRouteIs404AndWrongMethodIs405()
    {
        var host = Web.Host(Web.Api);
        var missing = await host.DispatchAsync(new UsHttpRequest("GET", "/yok"));
        Assert.Equal(404, missing.StatusCode);
        var wrong = await host.DispatchAsync(new UsHttpRequest("DELETE", "/kullanicilar/1"));
        Assert.Equal(405, wrong.StatusCode);
        Assert.True(wrong.Headers.ContainsKey("Allow"));
    }

    [Fact]
    public async Task NullResultIs204AndInternalErrorHidesStack()
    {
        var host = Web.Host("""
            kontrolcü X : "/x"
            {
                rota GET "/bos"
                fonksiyon bos(): nesne
                {
                    => içerikYok()
                }
                rota GET "/patlat"
                fonksiyon patlat(): nesne
                {
                    fırlat @Hata("pat")
                    => "x"
                }
            }
            """);
        var empty = await host.DispatchAsync(new UsHttpRequest("GET", "/x/bos"));
        Assert.Equal(204, empty.StatusCode);
        var boom = await host.DispatchAsync(new UsHttpRequest("GET", "/x/patlat"));
        Assert.Equal(500, boom.StatusCode);
        Assert.DoesNotContain("at ", boom.Body ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Stack", boom.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
