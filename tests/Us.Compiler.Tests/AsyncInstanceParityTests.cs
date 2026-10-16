using Us.Compiler;
using Us.Core;
using Us.Runtime;

namespace Us.Compiler.Tests;

/// <summary>M23 — async instance method compiler parity.</summary>
public class AsyncInstanceParityTests
{
    [Fact]
    public void AsyncInstanceReturnsPrimitive() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel afonksiyon değer(): tamsayı
            {
                => 42
            }
        }
        k: Kutu = @Kutu()
        sonuç: tamsayı = bekle k.değer()
        syaz "{sonuç}"
        """);

    [Fact]
    public void AsyncInstanceReadsFieldThroughBu() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel afonksiyon oku(): tamsayı
            {
                => bu.n
            }
        }
        k: Kutu = @Kutu()
        k.n = 7
        sonuç: tamsayı = bekle k.oku()
        syaz "{sonuç}"
        """);

    [Fact]
    public void AsyncInstanceWritesFieldThroughBu() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel afonksiyon yaz(değer: tamsayı)
            {
                bu.n = değer
            }
        }
        k: Kutu = @Kutu()
        bekle k.yaz(9)
        syaz "{k.n}"
        """);

    [Fact]
    public void CallerObservesMutationAfterBekle() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel afonksiyon artır()
            {
                bekle 10.milisaniye
                bu.n = bu.n + 1
            }
        }
        k: Kutu = @Kutu()
        k.n = 1
        bekle k.artır()
        syaz "{k.n}"
        """);

    [Fact]
    public void AsyncInstanceCallsSyncInstanceMethod() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel fonksiyon ekle(x: tamsayı)
            {
                bu.n = bu.n + x
            }
            genel afonksiyon yükle(x: tamsayı)
            {
                bekle 5.milisaniye
                bu.ekle(x)
            }
        }
        k: Kutu = @Kutu()
        k.n = 2
        bekle k.yükle(3)
        syaz "{k.n}"
        """);

    [Fact]
    public void AsyncInstanceAwaitsAnotherAsyncInstanceMethod() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel afonksiyon bir(): tamsayı
            {
                bekle 5.milisaniye
                => bu.n + 1
            }
            genel afonksiyon iki(): tamsayı
            {
                => bekle bu.bir()
            }
        }
        k: Kutu = @Kutu()
        k.n = 10
        sonuç: tamsayı = bekle k.iki()
        syaz "{sonuç}"
        """);

    [Fact]
    public void ArgumentsSurviveAsyncInvocation() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel afonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
            {
                bekle 5.milisaniye
                => a + b
            }
        }
        k: Kutu = @Kutu()
        sonuç: tamsayı = bekle k.topla(4, 5)
        syaz "{sonuç}"
        """);

    [Fact]
    public void LocalsSurviveBekle() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel afonksiyon hesap(): tamsayı
            {
                a: tamsayı = 3
                bekle 5.milisaniye
                => a + 4
            }
        }
        k: Kutu = @Kutu()
        sonuç: tamsayı = bekle k.hesap()
        syaz "{sonuç}"
        """);

    [Fact]
    public void ModelReferenceReturnIfSupported() => PhaseAParity.AssertParity("""
        model Sonuç
        {
            değer: tamsayı
        }
        sınıf Kutu
        {
            genel afonksiyon üret(): Sonuç
            {
                => @Sonuç { değer = 11 }
            }
        }
        k: Kutu = @Kutu()
        s: Sonuç = bekle k.üret()
        syaz "{s.değer}"
        """);

    [Fact]
    public void MultipleInstancesRemainIsolated() => PhaseAParity.AssertParity("""
        sınıf Kutu
        {
            genel n: tamsayı
            genel afonksiyon koy(değer: tamsayı)
            {
                bekle 5.milisaniye
                bu.n = değer
            }
        }
        a: Kutu = @Kutu()
        b: Kutu = @Kutu()
        bekle a.koy(1)
        bekle b.koy(2)
        syaz "{a.n}"
        syaz "{b.n}"
        """);
}

public class AsyncInstanceNegativeTests
{
    [Fact]
    public void AsyncPlusRefRemainsUs1900()
    {
        PhaseAParity.AssertEmitUnsupported("""
            sınıf Kutu
            {
                genel afonksiyon artır(ref sayı: tamsayı)
                {
                    sayı++
                }
            }
            k: Kutu = @Kutu()
            değer: tamsayı = 1
            bekle k.artır(ref değer)
            """, "eşzamansız");
    }

    [Fact]
    public void EmittedAsyncInstanceHasNoEvaluator()
    {
        var source = """
            sınıf Kutu
            {
                genel n: tamsayı
                genel afonksiyon yaz(değer: tamsayı)
                {
                    bu.n = değer
                }
            }
            k: Kutu = @Kutu()
            bekle k.yaz(3)
            syaz "{k.n}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var result = ParityHarness.Compare(source, ParityHarnessTests.RunInterpreter);
        Assert.True(result.Matched, result.FailureReason);
        Assert.NotNull(result.ArtifactPath);
        var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(result.ArtifactPath!));
        Assert.DoesNotContain("Evaluator", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
    }
}
