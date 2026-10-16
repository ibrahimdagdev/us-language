using System.Collections.Immutable;
using Us.Compiler;
using Us.Core;
using Us.Runtime;

namespace Us.Compiler.Tests;

/// <summary>M22 — nested JSON / liste&lt;model&gt; compiler parity.</summary>
public class JsonParityTests
{
    [Fact]
    public void PrimitiveSerializationMatches() => PhaseAParity.AssertParity("""
        syaz json.yaz(42)
        syaz json.yaz("Merhaba")
        syaz json.yaz(doğru)
        """);

    [Fact]
    public void SimpleModelRoundTripMatches() => PhaseAParity.AssertParity("""
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

    [Fact]
    public void NestedModelRoundTripMatches() => PhaseAParity.AssertParity("""
        model Adres
        {
            şehir: metin
        }
        model Kişi
        {
            ad: metin
            adres: Adres
        }
        kişi: Kişi = @Kişi { ad = "İbrahim", adres = @Adres { şehir = "İstanbul" } }
        okunan: Kişi = json.oku<Kişi>(json.yaz(kişi))
        syaz okunan.adres.şehir
        """);

    [Fact]
    public void ListeModelRoundTripMatches() => PhaseAParity.AssertParity("""
        model Adres
        {
            şehir: metin
        }
        model Kişi
        {
            ad: metin
            adres: Adres
        }
        kişiler: liste<Kişi> = json.oku<liste<Kişi>>("[{{ \"ad\": \"Ali\", \"adres\": {{ \"şehir\": \"Ankara\" }} }}]")
        syaz "{kişiler.adet}"
        syaz kişiler[0].ad
        syaz kişiler[0].adres.şehir
        """);

    [Fact]
    public void ModelContainingListePrimitiveMatches() => PhaseAParity.AssertParity("""
        model Sepet
        {
            kodlar: liste<metin>
        }
        s: Sepet = @Sepet { kodlar = ["a", "b"] }
        okunan: Sepet = json.oku<Sepet>(json.yaz(s))
        syaz "{okunan.kodlar.adet}"
        syaz okunan.kodlar[0]
        """);

    [Fact]
    public void ModelContainingListeModelMatches() => PhaseAParity.AssertParity("""
        model Satır
        {
            ad: metin
        }
        model Sipariş
        {
            kalemler: liste<Satır>
        }
        s: Sipariş = @Sipariş { kalemler = [@Satır { ad = "A" }, @Satır { ad = "B" }] }
        okunan: Sipariş = json.oku<Sipariş>(json.yaz(s))
        syaz "{okunan.kalemler.adet}"
        syaz okunan.kalemler[1].ad
        """);

    [Fact]
    public void NullableFieldNullMatches() => PhaseAParity.AssertParity("""
        model Kutu
        {
            ad: metin?
        }
        k: Kutu = json.oku<Kutu>("{{ \"ad\": null }}")
        syaz "tamam"
        """);

    [Fact]
    public void MissingNullableFieldMatches() => PhaseAParity.AssertParity("""
        model Kutu
        {
            ad: metin?
        }
        k: Kutu = json.oku<Kutu>("{{}}")
        syaz "tamam"
        """);

    [Fact]
    public void LiteralDefaultFillsMissingField() => PhaseAParity.AssertParity("""
        model Kutu
        {
            ad: metin : varsayılan("İbrahim")
        }
        k: Kutu = json.oku<Kutu>("{{}}")
        syaz k.ad
        """);

    [Fact]
    public void NestedAndListCombinedMatches() => PhaseAParity.AssertParity("""
        model Adres
        {
            şehir: metin
        }
        model Kişi
        {
            ad: metin
            adres: Adres
        }
        kişi: Kişi = @Kişi { ad = "İbrahim", adres = @Adres { şehir = "İstanbul" } }
        okunan: Kişi = json.oku<Kişi>(json.yaz(kişi))
        syaz okunan.adres.şehir
        kişiler: liste<Kişi> = json.oku<liste<Kişi>>("[{{ \"ad\": \"Ali\", \"adres\": {{ \"şehir\": \"Ankara\" }} }}]")
        syaz "{kişiler.adet}"
        """);
}

public class JsonNegativeTests
{
    [Fact]
    public void CompiledFieldNullIntoNonNullableIsUs1541()
    {
        var source = """
            model Kutu
            {
                ad: metin
            }
            k: Kutu = json.oku<Kutu>("{{ \"ad\": null }}")
            syaz "x"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        var interpreted = ProgramRunnerObservation(source);
        Assert.Contains(interpreted.Codes, code => code == UsRuntimeCodes.JsonNull);

        var bound = Compiler.BindOnly([SourceFile.FromText("json.us", source)]);
        Assert.False(bound.Diagnostics.HasErrors);
        var directory = Path.Combine(Path.GetTempPath(), "us-json1541-" + Guid.NewGuid().ToString("N"));
        try
        {
            var emit = new DotNetEmitBackend().Emit(bound, directory);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
            var process = EmittedProcess.Run(emit.ArtifactPath!, 15000);
            var output = process.StandardError + process.StandardOutput;
            Assert.NotEqual(0, process.ExitCode);
            // Unhandled UsRuntimeException prints Message (code is asserted in UsEmitJsonTests).
            Assert.Contains("null yalnız nullable alana yazılır", output, StringComparison.Ordinal);
            Assert.DoesNotContain("nullable olmayan hücreye yazılamaz", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CompiledCycleIsUs1529()
    {
        var source = """
            model Düğüm
            {
                sonraki: Düğüm?
            }
            a: Düğüm = @Düğüm { sonraki = ~ }
            a.sonraki = a
            syaz json.yaz(a)
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        var interpreted = ProgramRunnerObservation(source);
        Assert.Contains(interpreted.Codes, code => code == UsRuntimeCodes.JsonCycle);

        var bound = Compiler.BindOnly([SourceFile.FromText("cycle.us", source)]);
        Assert.False(bound.Diagnostics.HasErrors, string.Join("; ", bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));
        var directory = Path.Combine(Path.GetTempPath(), "us-json1529-" + Guid.NewGuid().ToString("N"));
        try
        {
            var emit = new DotNetEmitBackend().Emit(bound, directory);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
            var process = EmittedProcess.Run(emit.ArtifactPath!, 15000);
            var output = process.StandardError + process.StandardOutput;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("JSON döngüsel nesne yazamaz", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static (IReadOnlyList<string> Codes, string Stderr) ProgramRunnerObservation(string source)
    {
        var executed = Us.Interpreter.ProgramRunner.RunText("json.us", source);
        var codes = executed.Diagnostics.Select(item => item.Code).ToArray();
        var stderr = string.Join('\n', executed.Diagnostics.Select(item => item.Message));
        return (codes, stderr);
    }
}
