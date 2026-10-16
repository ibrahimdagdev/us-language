using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

public class FunctionEvalTests
{
    [Fact]
    public void SquareOfFourIsSixteenAndFrameCloses()
    {
        var result = Run.Clean("""
            fonksiyon kare(x: tamsayı): tamsayı
            {
                => x * x
            }
            sonuç: tamsayı = kare(4)
            syaz "{sonuç}"
            """);
        Assert.Equal("16", Assert.Single(result.Output.Lines));
        Assert.Equal(1, result.RootCellCount);
    }

    [Fact]
    public void ReturnSkipsTheRestOfTheBody()
    {
        var result = Run.Clean("""
            fonksiyon test(): tamsayı
            {
                => 10
                syaz "çalışmamalı"
            }
            syaz "{test()}"
            """);
        Assert.Equal("10", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void NestedCallAndForwardReference()
    {
        var result = Run.Clean("""
            fonksiyon a(): tamsayı
            {
                => b()
            }
            fonksiyon b(): tamsayı
            {
                => 10
            }
            syaz "{a()}"
            """);
        Assert.Equal("10", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void RecursionDoesNotShareLocals()
    {
        var result = Run.Clean("""
            fonksiyon faktoriyel(n: tamsayı): tamsayı
            {
                eğer n <= 1
                {
                    => 1
                }
                => n * faktoriyel(n - 1)
            }
            syaz "{faktoriyel(5)}"
            """);
        Assert.Equal("120", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void OverloadUsesTheBoundFunction()
    {
        var result = Run.Clean("""
            fonksiyon göster(x: tamsayı)
            {
                syaz "sayı"
            }
            fonksiyon göster(x: metin)
            {
                syaz "metin"
            }
            göster(10)
            göster("US")
            """);
        Assert.Equal(["sayı", "metin"], result.Output.Lines);
    }

    [Fact]
    public void ReturnInsideIfAndLoop()
    {
        var result = Run.Clean("""
            fonksiyon işaret(n: tamsayı): tamsayı
            {
                eğer n > 0
                {
                    => 10
                }
                => 0
            }
            fonksiyon ilk(): tamsayı
            {
                dön 1 ile 3 -> i
                {
                    => i
                }
                => 0
            }
            syaz "{işaret(1)}"
            syaz "{ilk()}"
            """);
        Assert.Equal(["10", "1"], result.Output.Lines);
    }

    [Fact]
    public void LocalStaysInTheCallAndGlobalRemainsVisible()
    {
        var result = Run.Clean("""
            dış: tamsayı = 4
            fonksiyon hesap(): tamsayı
            {
                yerel: tamsayı = dış + 1
                => yerel
            }
            syaz "{hesap()}"
            syaz "{dış}"
            """);
        Assert.Equal(["5", "4"], result.Output.Lines);
        Assert.Equal(1, result.RootCellCount);
    }
}

public class CallOrderTests
{
    [Fact]
    public void ArgumentsAreCopiedLeftToRight()
    {
        var result = Run.Clean("""
            sayaç: tamsayı = 0
            fonksiyon al(a: tamsayı, b: tamsayı): tamsayı
            {
                => a
            }
            sonuç: tamsayı = al(sayaç = 1, sayaç = 2)
            syaz "{sonuç}"
            syaz "{sayaç}"
            """);
        Assert.Equal(["1", "2"], result.Output.Lines);
    }

    [Fact]
    public void ValueParameterDoesNotChangeTheOuterNumber()
    {
        var result = Run.Clean("""
            fonksiyon artır(x: tamsayı): tamsayı
            {
                x++
                => x
            }
            dış: tamsayı = 6
            sonuç: tamsayı = artır(dış)
            syaz "{dış}"
            syaz "{sonuç}"
            """);
        Assert.Equal(["6", "7"], result.Output.Lines);
    }
}

public class SumEndToEndTests
{
    [Fact]
    public void AddPrintsThirty()
    {
        var result = Run.Clean("""
            fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
            {
                => a + b
            }
            sonuç: tamsayı = topla(10, 20)
            syaz "{sonuç}"
            """);
        Assert.Equal("30", Assert.Single(result.Output.Lines));
    }
}

public class RefOutEvalTests
{
    [Fact]
    public void RefWritesTheSameCell()
    {
        var result = Run.Clean("""
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 6
            artır(ref değer)
            syaz "{değer}"
            """);
        Assert.Equal("7", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void TwoRefsShareOneCell()
    {
        var result = Run.Clean("""
            fonksiyon ikisi(ref a: tamsayı, ref b: tamsayı)
            {
                a++
                b++
            }
            değer: tamsayı = 1
            ikisi(ref değer, ref değer)
            syaz "{değer}"
            """);
        Assert.Equal("3", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void OutputParameterReturnsQuotientAndRemainder()
    {
        var result = Run.Clean("""
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
        Assert.Equal(["3", "1"], result.Output.Lines);
    }

    [Fact]
    public void NestedDivisionByZeroKeepsUs1502()
    {
        var result = Run.Text("""
            fonksiyon b(): tamsayı
            {
                => 1 / 0
            }
            fonksiyon a(): tamsayı
            {
                => b()
            }
            a()
            """);
        Assert.True(result.Evaluated);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(UsRuntimeCodes.DivisionByZero, diagnostic.Code);
        Assert.Contains("a → b", diagnostic.Message, StringComparison.Ordinal);
    }
}

public class ModelEvalTests
{
    [Fact]
    public void InitializerReadsBoundFields()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
            }
            kullanıcı: Kullanıcı = @Kullanıcı
            {
                ad = "İbrahim"
                yaş = 38
            }
            syaz kullanıcı.ad
            syaz "{kullanıcı.yaş}"
            """);
        Assert.Equal(["İbrahim", "38"], result.Output.Lines);
    }

    [Fact]
    public void DefaultConstraintFillsTheOmittedField()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                aktif: mantıksal : varsayılan(doğru)
            }
            k: Kullanıcı = @Kullanıcı
            {
            }
            syaz "{k.aktif}"
            """);
        Assert.Equal("doğru", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void MissingRequiredFieldIsUs1512()
    {
        var result = Run.Text("""
            model Kullanıcı
            {
                ad: metin
            }
            k: Kullanıcı = @Kullanıcı
            {
            }
            """);
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.MissingField);
    }

    [Fact]
    public void NullableMemberIsNotEvaluatedWhenReceiverIsNull()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcı: Kullanıcı? = ~
            ad: metin? = kullanıcı?.ad
            syaz "{ad}"
            kullanıcı = @Kullanıcı
            {
                ad = "İbrahim"
            }
            ad = kullanıcı?.ad
            syaz "{ad}"
            """);
        Assert.Equal(["", "İbrahim"], result.Output.Lines);
    }
}

public class ModelEndToEndTests
{
    [Fact]
    public void SamplePrintsIbrahim()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcı: Kullanıcı = @Kullanıcı
            {
                ad = "İbrahim"
            }
            syaz kullanıcı.ad
            """);
        Assert.Equal("İbrahim", Assert.Single(result.Output.Lines));
    }
}

public class ConstructorEvalTests
{
    [Fact]
    public void ConstructorAssignsThis()
    {
        var result = Run.Clean("""
            sınıf Kullanıcı
            {
                ad: metin
                genel oluştur(ad: metin)
                {
                    bu.ad = ad
                }
                genel fonksiyon göster()
                {
                    syaz bu.ad
                }
            }
            k: Kullanıcı = @Kullanıcı("İbrahim")
            k.göster()
            """);
        Assert.Equal("İbrahim", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void ImplicitParameterlessConstructorExists()
    {
        var result = Run.Clean("""
            sınıf HesapMakinesi
            {
                genel fonksiyon hazır(): metin
                {
                    => "hazır"
                }
            }
            hesap: HesapMakinesi = @HesapMakinesi()
            syaz hesap.hazır()
            """);
        Assert.Equal("hazır", Assert.Single(result.Output.Lines));
    }
}

public class MethodEvalTests
{
    [Fact]
    public void InstanceMethodSeesThisAndStaticDoesNotNeedAnInstance()
    {
        var result = Run.Clean("""
            sınıf Servis
            {
                genel fonksiyon doğrula(id: tamsayı): mantıksal
                {
                    => id > 0
                }
                genel statik fonksiyon cevap(): tamsayı
                {
                    => 1
                }
            }
            s: Servis = @Servis()
            sonuç: mantıksal = s.doğrula(1)
            syaz "{sonuç}"
            syaz "{Servis.cevap()}"
            """);
        Assert.Equal(["doğru", "1"], result.Output.Lines);
    }

    [Fact]
    public void PrivateCallDoesNotRun()
    {
        var result = Run.Text("""
            sınıf Kapı
            {
                fonksiyon gizli(): tamsayı
                {
                    => 1
                }
            }
            k: Kapı = @Kapı()
            k.gizli()
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == "US0823");
    }
}

public class InheritanceEvalTests
{
    [Fact]
    public void DerivedSoundAndBaseField()
    {
        var result = Run.Clean("""
            sınıf Hayvan
            {
                korumalı ad: metin
                genel fonksiyon adGetir(): metin
                {
                    => ad
                }
            }
            sınıf Köpek : Hayvan
            {
                genel oluştur()
                {
                    ad = "Köpek"
                }
                genel fonksiyon ses(): metin
                {
                    => "hav"
                }
            }
            k: Köpek = @Köpek()
            syaz k.ses()
            syaz k.adGetir()
            """);
        Assert.Equal(["hav", "Köpek"], result.Output.Lines);
    }

    [Fact]
    public void TwoCountersDoNotShareFields()
    {
        var result = Run.Clean("""
            sınıf Sayaç
            {
                değer: tamsayı
                genel fonksiyon artır()
                {
                    değer++
                }
                genel fonksiyon getir(): tamsayı
                {
                    => değer
                }
            }
            a: Sayaç = @Sayaç()
            b: Sayaç = @Sayaç()
            a.artır()
            syaz "{a.getir()}"
            syaz "{b.getir()}"
            """);
        Assert.Equal(["1", "0"], result.Output.Lines);
    }
}

public class InterfaceEvalTests
{
    [Fact]
    public void InterfaceReceiverDispatchesToTheClass()
    {
        var result = Run.Clean("""
            arayüz Uçabilir
            {
                fonksiyon uç()
            }
            sınıf Kuş : Uçabilir
            {
                genel fonksiyon uç()
                {
                    syaz "Uçuyor"
                }
            }
            u: Uçabilir = @Kuş()
            u.uç()
            """);
        Assert.Equal("Uçuyor", Assert.Single(result.Output.Lines));
    }
}

public class BaseCallTests
{
    [Fact]
    public void BaseCallDoesNotReenterTheOverride()
    {
        var result = Run.Clean("""
            sınıf Hayvan
            {
                genel sanal fonksiyon ses()
                {
                    syaz "Hayvan"
                }
            }
            sınıf Köpek : Hayvan
            {
                genel yerinegeç fonksiyon ses()
                {
                    temel.ses()
                    syaz "Köpek"
                }
            }
            k: Köpek = @Köpek()
            k.ses()
            hayvan: Hayvan = k
            hayvan.ses()
            """);
        Assert.Equal(["Hayvan", "Köpek", "Hayvan", "Köpek"], result.Output.Lines);
    }

    [Fact]
    public void ParameterizedBaseWithoutCallSyntaxIsRejected()
    {
        var result = Run.Text("""
            sınıf Hayvan
            {
                genel oluştur(ad: metin)
                {
                }
            }
            sınıf Köpek : Hayvan
            {
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == "US0837");
    }
}

public class GenericEvalTests
{
    [Fact]
    public void ClosedModelsStaySeparate()
    {
        var result = Run.Clean("""
            model Sonuç<T>
            {
                başarılı: mantıksal
                veri: T?
            }
            yazı: Sonuç<metin> = @Sonuç<metin>
            {
                başarılı = doğru
                veri = "Tamam"
            }
            sayı: Sonuç<tamsayı> = @Sonuç<tamsayı>
            {
                başarılı = doğru
                veri = 4
            }
            syaz "{yazı.veri}"
            syaz "{sayı.veri}"
            """);
        Assert.Equal(["Tamam", "4"], result.Output.Lines);
    }

    [Fact]
    public void GenericClassAndFunctionUseTheBoundSubstitution()
    {
        var result = Run.Clean("""
            sınıf Depo<T>
            {
                öğe: T?
                genel fonksiyon ata(değer: T)
                {
                    öğe = değer
                }
                genel fonksiyon getir(): T?
                {
                    => öğe
                }
            }
            fonksiyon kimlik<T>(değer: T): T
            {
                => değer
            }
            depo: Depo<tamsayı> = @Depo<tamsayı>()
            depo.ata(kimlik(4))
            ad: metin = kimlik("İbrahim")
            syaz "{depo.getir()}"
            syaz ad
            """);
        Assert.Equal(["4", "İbrahim"], result.Output.Lines);
    }

    [Fact]
    public void WritingTheWrongPrimitiveIsUs1508()
    {
        var type = new SourceTypeSymbol("Depo", SourceTypeKind.Class);
        var field = new FieldSymbol("veri", new PrimitiveTypeSymbol("tamsayı"), Visibility.Public) { DeclaringType = type };
        type.Fields.Add(field);
        var instance = UsInstance.Allocate(type);
        var exception = Assert.Throws<UsRuntimeException>(() => instance.Write(field, UsValue.FromText("metin"), type, 1, 1));
        Assert.Equal(UsRuntimeCodes.GenericStore, exception.Code);
    }

    [Fact]
    public void FailedConstraintDoesNotRun()
    {
        var result = Run.Text("""
            arayüz İşaret
            {
                fonksiyon işaret()
            }
            model Kutu<T: İşaret>
            {
                değer: T?
            }
            k: Kutu<tamsayı> = @Kutu<tamsayı>
            {
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == "US0830");
    }
}

public class LambdaEvalTests
{
    [Fact]
    public void DoubleOfFourIsEight()
    {
        var result = Run.Clean("""
            çarp: tamsayı = (x: tamsayı) => x * 2
            syaz "{çarp(4)}"
            """);
        Assert.Equal("8", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void CaptureSeesTheOuterCell()
    {
        var result = Run.Clean("""
            dış: tamsayı = 1
            hesap: tamsayı = (x: tamsayı) => x + dış
            dış = 5
            syaz "{hesap(3)}"
            """);
        Assert.Equal("8", Assert.Single(result.Output.Lines));
    }
}

public class EnumEvalTests
{
    [Fact]
    public void WaitingArmRuns()
    {
        var result = Run.Clean("""
            seçenek SiparişDurumu
            {
                Bekliyor
                Tamam
            }
            durum: SiparişDurumu = SiparişDurumu.Bekliyor
            seç durum
            {
                SiparişDurumu.Bekliyor =>
                {
                    syaz "Bekliyor"
                }
                _ =>
                {
                    syaz "diğer"
                }
            }
            """);
        Assert.Equal("Bekliyor", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void UnmatchedSelectIsUs1509()
    {
        var result = Run.Text("""
            seç 3
            {
                1 =>
                {
                    syaz "bir"
                }
            }
            """);
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.SelectMiss);
    }
}

public class AbstractEvalTests
{
    [Fact]
    public void AbstractAllocationIsUs1510AndANormalClassStillRuns()
    {
        var type = new SourceTypeSymbol("Soyut", SourceTypeKind.Class) { IsAbstract = true };
        var exception = Assert.Throws<UsRuntimeException>(() => UsInstance.Allocate(type));
        Assert.Equal(UsRuntimeCodes.AbstractRuntime, exception.Code);
        var result = Run.Clean("""
            mühürlü sınıf Kapı
            {
            }
            k: Kapı = @Kapı()
            syaz "açık"
            """);
        Assert.Equal("açık", Assert.Single(result.Output.Lines));
    }
}

public class CopyEvalTests
{
    [Fact]
    public void CopyLeavesTheSourceAlone()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            kaynak: Kullanıcı = @Kullanıcı
            {
                ad = "İbrahim"
            }
            güncel: Kullanıcı = kaynak ile
            {
                ad = "Ahmet"
            }
            syaz güncel.ad
            syaz kaynak.ad
            """);
        Assert.Equal(["Ahmet", "İbrahim"], result.Output.Lines);
    }

    [Fact]
    public void ClassCopyIsRejected()
    {
        var result = Run.Text("""
            sınıf Kapı
            {
                ad: metin
            }
            a: Kapı = @Kapı()
            b: Kapı = a ile
            {
                ad = "x"
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == "US0838");
    }
}
