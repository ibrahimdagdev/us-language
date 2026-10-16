using Us.Compiler;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Compiler.Tests;

public class StdlibParityTests
{
    private static readonly DateTime FixedInstant = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Unspecified);
    private const string ClockEnv = "2026-10-07T00:00:00";

    [Fact]
    public void ClockAndDurationMatch()
    {
        AssertHostedParity(
            """
            son: tarihsaat = şimdi + 30.dakika
            fark: süre = son - şimdi
            syaz "{bugün}"
            syaz "{şimdi}"
            syaz "{son}"
            syaz "{fark}"
            syaz "{şimdi < son}"
            """,
            new UsHost { Clock = new FixedClock(FixedInstant) },
            new Dictionary<string, string> { ["US_TEST_CLOCK"] = ClockEnv });
    }

    [Fact]
    public void FilePathRoundTripMatches()
    {
        PhaseAParity.AssertParity("""
            dosya.yaz("a.txt", "merhaba")
            syaz dosya.oku("a.txt")
            syaz yol.birleştir("klasor", "a.txt")
            """);
    }

    [Fact]
    public void JsonModelRoundTripMatches()
    {
        PhaseAParity.AssertParity("""
            model Kutu
            {
                ad: metin
                n: tamsayı
            }
            k: Kutu = @Kutu { ad = "Ali", n = 3 }
            okunan: Kutu = json.oku<Kutu>(json.yaz(k))
            syaz okunan.ad
            syaz "{okunan.n}"
            """);
    }

    [Fact]
    public void TurkishTextAndLogMatch()
    {
        PhaseAParity.AssertParity("""
            syaz "  İbrahim  ".kırp()
            syaz "istanbul".büyük()
            log.bilgi("x")
            syaz "ok"
            """);
    }

    [Fact]
    public void ConfigBindDoesNotCrashWithEnv()
    {
        var config = """{ "MongoDb": { "sunucu": "localhost", "port": 27017 } }""";
        AssertHostedParity(
            """
            model MongoDbAyar : Ayar("MongoDb")
            {
                sunucu: metin
                port: tamsayı
            }
            bağla ayar: MongoDbAyar
            syaz "ayar"
            """,
            new UsHost { Configuration = new MemoryConfiguration(config) },
            new Dictionary<string, string> { ["US_TEST_CONFIG"] = config });
    }

    private static void AssertHostedParity(string source, UsHost host, IReadOnlyDictionary<string, string> environment)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        InterpreterObservation Run(string text)
        {
            var executed = ProgramRunner.RunText("parity.us", text, host);
            if (!executed.Evaluated)
            {
                var stderr = string.Join('\n', executed.Diagnostics.Select(item => item.Message));
                return new InterpreterObservation(1, string.Empty, stderr);
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
        if (output.Pending.Length > 0)
        {
            text += '\n' + output.Pending;
        }
        else
        {
            text += '\n';
        }

        return text;
    }
}
