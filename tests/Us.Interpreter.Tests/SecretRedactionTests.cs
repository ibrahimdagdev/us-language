using Us.Interpreter;
using Us.Runtime;

namespace Us.Interpreter.Tests;

public class SecretRedactionTests
{
    [Fact]
    public void PasswordFieldIsMaskedInLog()
    {
        var log = new ListLog();
        var host = new UsHost { Log = log };

        var result = ProgramRunner.RunText("deneme.us", """
            sınıf Güvenlik
            {
                genel parola: metin
                genel apiToken: metin
                genel sunucu: metin
                genel oluştur()
                {
                    parola = "acik-gizli-123"
                    apiToken = "token-abc"
                    sunucu = "yerel"
                }
            }
            g: Güvenlik = @Güvenlik()
            log.bilgi(g.parola)
            log.bilgi(g.apiToken)
            log.bilgi(g.sunucu)
            """, host);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, log.Records.Count(item => item.Level == UsLogLevel.Information && item.Message == SecretRedactor.Mask));
        Assert.DoesNotContain(log.Records, item => item.Message.Contains("acik-gizli-123", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Records, item => item.Message.Contains("token-abc", StringComparison.Ordinal));
        Assert.Contains(log.Records, item => item.Message == "yerel");
    }

    [Fact]
    public void MissingConfigDiagnosticDoesNotCarryFileContent()
    {
        var secret = "super-gizli-icerik-xyz";
        var result = ProgramRunner.RunText("deneme.us", """
            model MongoDbAyar : Ayar("MongoDb") { sunucu: metin }
            bağla ayar: MongoDbAyar
            """, new UsHost { Configuration = new MemoryConfiguration(null) });

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(UsRuntimeCodes.MissingConfig, diagnostic.Code);
        Assert.DoesNotContain(secret, diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('{', diagnostic.Message);
        Assert.Equal("uygulamaAyar.json bulunamadı.", diagnostic.Message);
    }

    [Fact]
    public void AuthorizationHeaderIsMaskedInLogCopy()
    {
        var log = new ListLog();
        var transport = new FakeHttpTransport();
        transport.Register("GET", "u", new UsHttpClientResponse(200, "{}", "application/json", new Dictionary<string, string>()));
        var host = new UsHost { Log = log, Transport = transport };

        var result = ProgramRunner.RunText("deneme.us", """
            bekle http.başlık("Authorization", "Bearer gizli-token-999").GET("u")
            """, host);

        Assert.Empty(result.Diagnostics);
        Assert.Equal("Bearer gizli-token-999", transport.LastHeaders!["Authorization"]);
        var copy = Assert.Single(log.Records, item => item.Level == UsLogLevel.Trace && item.Message.StartsWith("HTTP GET", StringComparison.Ordinal));
        Assert.Contains("Authorization=***", copy.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("gizli-token-999", copy.Message, StringComparison.Ordinal);
    }
}
