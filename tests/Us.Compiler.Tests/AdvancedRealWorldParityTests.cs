using System.Text;
using System.Text.Json;
using Us.Compiler;
using Us.Core;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Compiler.Tests;

/// <summary>
/// TASK-266 advanced real-world proof: supported M16.5 surface compiled to DLL, separate process, no Evaluator.
/// </summary>
public class AdvancedRealWorldParityTests
{
    [Fact]
    public void AdvancedSurfaceEmitsRunsAndMatchesInterpreter()
    {
        const string httpJson = """{"GET https://adv/x":{"status":200,"body":"{\"kod\":\"OK\"}"}}""";
        var source = """
            afonksiyon gecik(): tamsayı
            {
                bekle 5.milisaniye
                => 2
            }
            sınıf Sayaç
            {
                genel olay tik: metin
                genel fonksiyon bildir(değer: metin)
                {
                    syaz değer
                }
            }
            model Paket { kod: metin }
            model Cfg : Ayar("C") { ad: metin }
            servis Depo : geçici
            {
                genel n: tamsayı
                genel oluştur() { n = 4 }
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
            a: tamsayı = 0
            b: tamsayı = 0
            eşişlem
            {
                a = bekle gecik()
                b = bekle gecik()
            }
            s: Sayaç = @Sayaç()
            s.tik += s.bildir
            s.tik.tetikle("evt")
            bağla ayar: Cfg
            bağla servis: Depo
            bağla db: Db
            işlem
            {
                bekle db.satırlar.ekle(@Satır { ad = "r" })
                onayla
            }
            p: Paket = bekle http.GET("https://adv/x").json()
            satırlar: liste<Satır> = bekle db.satırlar.tümü()
            syaz "{a + b}"
            syaz p.kod
            syaz "{servis.n}"
            syaz "{satırlar.adet}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        var host = new UsHost
        {
            Transport = UsEmitHttp.ParseTestHttpMap(httpJson),
            Configuration = new MemoryConfiguration("""{"C":{"ad":"cfg"}}"""),
        };
        var environment = new Dictionary<string, string>
        {
            ["US_TEST_HTTP"] = httpJson,
            ["US_TEST_CONFIG"] = """{"C":{"ad":"cfg"}}""",
        };

        var result = ParityHarness.Compare(
            source,
            text =>
            {
                var executed = ProgramRunner.RunText("adv.us", text, host);
                if (!executed.Evaluated)
                {
                    return new InterpreterObservation(1, string.Empty, string.Join('\n', executed.Diagnostics.Select(item => item.Message)));
                }

                var stdout = executed.Output.Lines.Count == 0
                    ? executed.Output.Pending
                    : string.Join('\n', executed.Output.Lines) + "\n";
                return new InterpreterObservation(0, stdout, string.Empty);
            },
            environment: environment);

        Assert.True(result.Matched, result.FailureReason + "\ninterp=" + result.InterpreterStdout + "\ncompiled=" + result.CompiledStdout);
        Assert.Equal(0, result.CompiledExitCode);
        Assert.NotNull(result.ArtifactPath);
        var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(result.ArtifactPath));
        Assert.DoesNotContain("Us.Interpreter.Evaluator", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
        Assert.Contains("evt", result.CompiledStdout, StringComparison.Ordinal);
        Assert.Contains("OK", result.CompiledStdout, StringComparison.Ordinal);
    }
}
