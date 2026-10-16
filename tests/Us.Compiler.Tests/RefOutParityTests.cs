using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

/// <summary>M20/M21 — ref/çıktı ByRef emit parity and unsupported gates.</summary>
public class RefOutParityTests
{
    [Fact]
    public void RefModifiesCallerLocal() => PhaseAParity.AssertParity("""
        fonksiyon artır(ref sayı: tamsayı)
        {
            sayı++
        }
        değer: tamsayı = 6
        artır(ref değer)
        syaz "{değer}"
        """);

    [Fact]
    public void CiktiWritesCallerDestination() => PhaseAParity.AssertParity("""
        fonksiyon böl(a: tamsayı, b: tamsayı, çıktı kalan: tamsayı): tamsayı
        {
            kalan = a % b
            => a / b
        }
        kalan: tamsayı
        sonuç: tamsayı = böl(10, 3, çıktı kalan)
        syaz "{sonuç}"
        syaz "{kalan}"
        """);

    [Fact]
    public void TwoRefsShareOneCell() => PhaseAParity.AssertParity("""
        fonksiyon ikisi(ref a: tamsayı, ref b: tamsayı)
        {
            a++
            b++
        }
        değer: tamsayı = 1
        ikisi(ref değer, ref değer)
        syaz "{değer}"
        """);

    [Fact]
    public void RefAndNormalParameters() => PhaseAParity.AssertParity("""
        fonksiyon ekle(ref hedef: tamsayı, miktar: tamsayı)
        {
            hedef = hedef + miktar
        }
        değer: tamsayı = 10
        ekle(ref değer, 5)
        syaz "{değer}"
        """);

    [Fact]
    public void NestedRefForwarding() => PhaseAParity.AssertParity("""
        fonksiyon iç(ref x: tamsayı)
        {
            x = x + 1
        }
        fonksiyon dış(ref y: tamsayı)
        {
            iç(ref y)
            y = y + 1
        }
        değer: tamsayı = 1
        dış(ref değer)
        syaz "{değer}"
        """);

    [Fact]
    public void NestedCiktiForwarding() => PhaseAParity.AssertParity("""
        fonksiyon yazDeğer(çıktı hedef: tamsayı)
        {
            hedef = 42
        }
        fonksiyon köprü(çıktı hedef: tamsayı)
        {
            yazDeğer(çıktı hedef)
        }
        sonuç: tamsayı
        köprü(çıktı sonuç)
        syaz "{sonuç}"
        """);

    [Fact]
    public void NullableRefWhenValid() => PhaseAParity.AssertParity("""
        fonksiyon koy(ref kutu: tamsayı?)
        {
            kutu = 7
        }
        değer: tamsayı? = ~
        koy(ref değer)
        syaz "{değer}"
        """);

    [Fact]
    public void ModelReferenceRef() => PhaseAParity.AssertParity("""
        model Kutu
        {
            ad: metin
        }
        fonksiyon değiştir(ref k: Kutu)
        {
            k = @Kutu { ad = "B" }
        }
        kutu: Kutu = @Kutu { ad = "A" }
        değiştir(ref kutu)
        syaz "{kutu.ad}"
        """);

    [Fact]
    public void NormalFunctionsUnaffected() => PhaseAParity.AssertParity("""
        fonksiyon çift(n: tamsayı): tamsayı
        {
            => n * 2
        }
        syaz "{çift(21)}"
        """);

    [Fact]
    public void FieldRefTargetIsExplicitlyUnsupported() =>
        PhaseAParity.AssertEmitUnsupported("""
            model Kutu
            {
                sayı: tamsayı
            }
            fonksiyon artır(ref x: tamsayı)
            {
                x++
            }
            k: Kutu = @Kutu { sayı = 1 }
            artır(ref k.sayı)
            syaz "{k.sayı}"
            """, "Alan ref/çıktı");

    [Fact]
    public void AsyncWithRefIsExplicitlyUnsupported() =>
        PhaseAParity.AssertEmitUnsupported("""
            afonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 1
            bekle artır(ref değer)
            syaz "{değer}"
            """, "eşzamansız");

    [Fact]
    public void EmittedAssemblyHasNoEvaluator()
    {
        var source = """
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 6
            artır(ref değer)
            syaz "{değer}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        var result = ParityHarness.Compare(source, ParityHarnessTests.RunInterpreter);
        Assert.True(result.Matched, result.FailureReason);
        Assert.NotNull(result.ArtifactPath);
        var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(result.ArtifactPath!));
        Assert.DoesNotContain("Evaluator", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
    }
}

public class RefOutEmitGateTests
{
    [Fact]
    public void SilentByValueCompilationCannotHappenForSupportedRef()
    {
        // If ByRef were incorrectly lowered by value, caller would still see 6.
        var result = ParityHarness.Compare("""
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 6
            artır(ref değer)
            syaz "{değer}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal), ParityHarnessTests.RunInterpreter);
        Assert.True(result.Matched, result.FailureReason);
        Assert.Contains("7", result.CompiledStdout, StringComparison.Ordinal);
        Assert.DoesNotContain("6\n", result.CompiledStdout.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
