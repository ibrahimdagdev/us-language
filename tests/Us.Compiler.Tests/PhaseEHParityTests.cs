using System.Collections.Immutable;
using System.Text;
using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

public class ThrowParityTests
{
    [Fact]
    public void TryCatchFinallyMatches() => PhaseAParity.AssertParity("""
        sayaç: tamsayı = 0
        dene
        {
            fırlat @Hata("Bir hata")
        }
        yakala hata
        {
            syaz "Yakalandı"
        }
        sonunda
        {
            sayaç++
        }
        syaz "{sayaç}"
        """);
}

public class RealWorldCompilationProofTests
{
    [Fact]
    public void MultiFileProjectEmitsAndRunsWithoutInterpreter()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-m155-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Keep sources unindented — binder diagnostics include surrounding spaces when names break.
            var modeller =
                "model Siparis\n{\n    kod: metin\n    tutar: tamsayı\n}\n\n" +
                "fonksiyon faktor(n: tamsayı): tamsayı\n{\n    eğer n <= 1\n    {\n        => 1\n    }\n    => n * faktor(n - 1)\n}\n";
            var ana =
                "toplam: tamsayı = 0\n" +
                "dön 1 ile 3 -> i\n{\n    toplam += i\n}\n\n" +
                "siparis: Siparis = @Siparis\n{\n    kod = \"A1\"\n    tutar = toplam * faktor(3)\n}\n\n" +
                "kalemler: liste<tamsayı> = [10, 20, 30]\n" +
                "secilen: liste<tamsayı> = kalemler.filtrele(^ >= 20)\n\n" +
                "eğer siparis.tutar > 0\n{\n    syaz \"Siparis {siparis.kod}: {siparis.tutar}\"\n    syaz \"Secilen: {secilen.adet}\"\n}\n" +
                "değilse\n{\n    syaz \"Bos\"\n}\n\n" +
                "dene\n{\n    eğer secilen.adet == 0\n    {\n        fırlat @Hata(\"yok\")\n    }\n}\n" +
                "yakala hata\n{\n    syaz \"Hata\"\n}\n" +
                "sonunda\n{\n    syaz \"Bitti\"\n}\n";
            File.WriteAllText(Path.Combine(root, "Modeller.us"), modeller, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "ana.us"), ana, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var files = new[]
            {
                SourceFile.FromText(Path.Combine(root, "Modeller.us"), modeller),
                SourceFile.FromText(Path.Combine(root, "ana.us"), ana),
            };
            var bound = Compiler.BindOnly(files);
            Assert.False(bound.Diagnostics.HasErrors, string.Join("; ", bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));

            var outDir = Path.Combine(root, "cikti");
            var emit = new DotNetEmitBackend().Emit(bound, outDir);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
            Assert.NotNull(emit.ArtifactPath);

            var bytes = File.ReadAllBytes(emit.ArtifactPath);
            var ascii = Encoding.ASCII.GetString(bytes);
            Assert.DoesNotContain("Evaluator", ascii, StringComparison.Ordinal);
            Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);

            var process = EmittedProcess.Run(emit.ArtifactPath);
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Siparis A1: 36", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Secilen: 2", process.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("Bitti", process.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class ArchitectureGuardTests
{
    [Fact]
    public void EmittedArtifactDoesNotEmbedInterpreterTypes()
    {
        var source = """
            syaz "guard"
            """;
        var bound = Compiler.BindOnly([SourceFile.FromText("g.us", source)]);
        var dir = Path.Combine(Path.GetTempPath(), "us-guard-" + Guid.NewGuid().ToString("N"));
        var emit = new DotNetEmitBackend().Emit(bound, dir);
        Assert.True(emit.Success);
        var text = Encoding.ASCII.GetString(File.ReadAllBytes(emit.ArtifactPath!));
        Assert.DoesNotContain("Us.Interpreter.Evaluator", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramRunner", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(dir, "Us.Runtime.dll")));
    }
}
