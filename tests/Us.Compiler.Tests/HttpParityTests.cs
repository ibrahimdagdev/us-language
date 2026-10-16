using System.Text.Json;
using Us.Compiler;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Compiler.Tests;

public class HttpParityTests
{
    [Fact]
    public void OutboundGetJsonMatchesFakeTransport()
    {
        const string url = "https://ornek/a";
        var map = new Dictionary<string, object>
        {
            [$"GET {url}"] = new { status = 200, body = """{"ad":"Ali"}""" },
        };
        var json = JsonSerializer.Serialize(map);
        var transport = UsEmitHttp.ParseTestHttpMap(json);
        AssertHostedParity(
            """
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcı: Kullanıcı = bekle http.GET("https://ornek/a").json()
            syaz kullanıcı.ad
            """,
            new UsHost { Transport = transport },
            new Dictionary<string, string> { ["US_TEST_HTTP"] = json });
    }

    [Fact]
    public void HeaderAndGetReachTransport()
    {
        const string url = "https://ornek/p";
        var map = new Dictionary<string, object>
        {
            [$"GET {url}"] = new { status = 200, body = """{"ok":true}""" },
        };
        var json = JsonSerializer.Serialize(map);
        var transport = UsEmitHttp.ParseTestHttpMap(json);
        AssertHostedParity(
            """
            model Yanıt { ok: mantıksal }
            y: Yanıt = bekle http.başlık("X", "1").GET("https://ornek/p").json()
            syaz "{y.ok}"
            """,
            new UsHost { Transport = transport },
            new Dictionary<string, string> { ["US_TEST_HTTP"] = json });
    }

    [Fact]
    public void CompiledRouteHandlerDispatchesWithoutKestrel()
    {
        AssertRouteParity(
            """
            kontrolcü Merhaba : "/api"
            {
                rota GET "/selam"
                fonksiyon selam(): metin
                {
                    => "merhaba"
                }
            }
            """,
            "GET /api/selam",
            expectedStatus: 200,
            expectedBody: "merhaba");
    }

    [Fact]
    public void CompiledRouteStatusHelperMatches()
    {
        AssertRouteParity(
            """
            kontrolcü X : "/x"
            {
                rota GET "/a"
                fonksiyon a(): nesne
                {
                    => tamam("ok")
                }
            }
            """,
            "GET /x/a",
            expectedStatus: 200,
            expectedBody: "ok");
    }

    private static void AssertRouteParity(string source, string route, int expectedStatus, string expectedBody)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        InterpreterObservation Run(string text)
        {
            var host = UsWebHost.FromText("parity.us", text);
            var parts = route.Split(' ', 2);
            var response = host.DispatchAsync(new UsHttpRequest(parts[0], parts[1])).AsTask().GetAwaiter().GetResult();
            return new InterpreterObservation(0, response.StatusCode + "\n" + (response.Body ?? string.Empty) + "\n", string.Empty);
        }

        var result = ParityHarness.Compare(
            normalized,
            Run,
            environment: new Dictionary<string, string> { ["US_TEST_ROUTE"] = route });
        Assert.True(result.Matched, result.FailureReason + "\ninterp=" + result.InterpreterStdout + "\ncompiled=" + result.CompiledStdout);
        Assert.Contains(expectedStatus.ToString(), result.CompiledStdout, StringComparison.Ordinal);
        Assert.Contains(expectedBody, result.CompiledStdout, StringComparison.Ordinal);
    }

    private static void AssertHostedParity(string source, UsHost host, IReadOnlyDictionary<string, string> environment)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        InterpreterObservation Run(string text)
        {
            var executed = ProgramRunner.RunText("parity.us", text, host);
            if (!executed.Evaluated)
            {
                return new InterpreterObservation(1, string.Empty, string.Join('\n', executed.Diagnostics.Select(item => item.Message)));
            }

            return new InterpreterObservation(0, FormatStdout(executed.Output), executed.Diagnostics.Count == 0
                ? string.Empty
                : string.Join('\n', executed.Diagnostics.Select(item => item.Message)));
        }

        var result = ParityHarness.Compare(normalized, Run, environment: environment);
        Assert.True(result.Matched, result.FailureReason + "\ninterp=" + result.InterpreterStdout + "\ncompiled=" + result.CompiledStdout);
    }

    private static string FormatStdout(ListOutput output)
    {
        if (output.Lines.Count == 0)
        {
            return output.Pending;
        }

        var text = string.Join('\n', output.Lines);
        text += output.Pending.Length > 0 ? '\n' + output.Pending : "\n";
        return text;
    }
}

public class PhaseGGateTests
{
    [Fact]
    public void StdlibDiDbAndHttpTogether()
    {
        const string httpJson = """{"GET https://g/a":{"status":200,"body":"{\"ad\":\"Z\"}"}}""";
        var transport = UsEmitHttp.ParseTestHttpMap(httpJson);
        var host = new UsHost
        {
            Clock = new FixedClock(new DateTime(2026, 10, 7)),
            Transport = transport,
            Configuration = new MemoryConfiguration("""{"S":{"ad":"cfg"}}"""),
        };
        HttpParityTestsAssert(
            """
            model Kullanıcı { ad: metin }
            model SAyar : Ayar("S") { ad: metin }
            servis Kutu : geçici
            {
                genel n: tamsayı
                genel oluştur() { n = 1 }
            }
            model Satır
            {
                id: tamsayı : anahtar
                ad: metin
            }
            veritabanı Db
            {
                tablo satırlar: Satır
            }
            bağla ayar: SAyar
            bağla servis: Kutu
            bağla db: Db
            işlem
            {
                bekle db.satırlar.ekle(@Satır { ad = "a" })
                onayla
            }
            k: Kullanıcı = bekle http.GET("https://g/a").json()
            syaz "{bugün}"
            syaz k.ad
            syaz "{servis.n}"
            """,
            host,
            new Dictionary<string, string>
            {
                ["US_TEST_CLOCK"] = "2026-10-07T00:00:00",
                ["US_TEST_CONFIG"] = """{"S":{"ad":"cfg"}}""",
                ["US_TEST_HTTP"] = httpJson,
            });
    }

    private static void HttpParityTestsAssert(string source, UsHost host, IReadOnlyDictionary<string, string> environment)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var result = ParityHarness.Compare(
            normalized,
            text =>
            {
                var executed = ProgramRunner.RunText("parity.us", text, host);
                if (!executed.Evaluated)
                {
                    return new InterpreterObservation(1, string.Empty, string.Join('\n', executed.Diagnostics.Select(item => item.Message)));
                }

                var stdout = executed.Output.Lines.Count == 0
                    ? executed.Output.Pending
                    : string.Join('\n', executed.Output.Lines) + (executed.Output.Pending.Length > 0 ? "\n" + executed.Output.Pending : "\n");
                return new InterpreterObservation(0, stdout, string.Empty);
            },
            environment: environment);
        Assert.True(result.Matched, result.FailureReason + "\ninterp=" + result.InterpreterStdout + "\ncompiled=" + result.CompiledStdout);
    }
}
