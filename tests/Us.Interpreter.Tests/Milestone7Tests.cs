using Us.Core;
using Us.Interpreter;
using Us.Lexer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

public class ListLiteralTests
{
    [Fact]
    public void TwoNamesHaveLengthTwo()
    {
        var result = Run.Clean("""
            isimler: liste<metin> = ["Ali", "Ayşe"]
            syaz "{isimler.adet}"
            """);
        Assert.Equal("2", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void UntypedEmptyLiteralIsUs0809()
    {
        var result = Run.Text("x = []");
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.IllegalConversion);
    }

    [Fact]
    public void ElementsEvaluateLeftToRight()
    {
        var result = Run.Clean("""
            fonksiyon bir(): tamsayı
            {
                syaz "1"
                => 1
            }
            fonksiyon iki(): tamsayı
            {
                syaz "2"
                => 2
            }
            değerler: liste<tamsayı> = [bir(), iki()]
            syaz "{değerler.adet}"
            """);
        Assert.Equal(["1", "2", "2"], result.Output.Lines);
    }
}

public class CollectionKindTests
{
    [Fact]
    public void SetKeepsOneCopyAndArrayRejectsAdd()
    {
        var set = Run.Clean("""
            değerler: küme<tamsayı> = [1, 1, 2]
            syaz "{değerler.adet}"
            """);
        Assert.Equal("2", Assert.Single(set.Output.Lines));
        var array = Run.Text("""
            değerler: dizi<tamsayı> = [1, 2]
            değerler.ekle(3)
            """);
        Assert.True(array.Evaluated);
        Assert.Contains(array.Diagnostics, item => item.Code == UsRuntimeCodes.FixedCollection);
    }

    [Fact]
    public void DictionaryAddAndSequencePreservesOrder()
    {
        var dictionary = Run.Clean("""
            kayıt: sözlük<metin, tamsayı> = []
            kayıt.ekle("a", 1)
            syaz "{kayıt.adet}"
            """);
        Assert.Equal("1", Assert.Single(dictionary.Output.Lines));
        var sequence = Run.Clean("""
            değerler: sliste<tamsayı> = [1, 2]
            değerler.ekle(3)
            dön değerler -> sayı
            {
                syaz "{sayı}"
            }
            """);
        Assert.Equal(["1", "2", "3"], sequence.Output.Lines);
    }
}

public class CollectionMemberTests
{
    [Fact]
    public void AddIncreasesCountAndReverseLeavesTheSource()
    {
        var result = Run.Clean("""
            sayılar: liste<tamsayı> = [1, 2]
            sayılar.ekle(3)
            ters: liste<tamsayı> = sayılar.tersÇevir()
            syaz "{sayılar.adet}"
            syaz "{sayılar[0]}"
            syaz "{ters[0]}"
            """);
        Assert.Equal(["3", "1", "3"], result.Output.Lines);
    }

    [Fact]
    public void IndexAssignmentMutatesTheSameList()
    {
        var result = Run.Clean("""
            sayılar: liste<tamsayı> = [1, 2, 3]
            sayılar[0] = 10
            syaz "{sayılar[0]}"
            syaz "{sayılar.içerir(10)}"
            sayılar.kaldır(2)
            syaz "{sayılar.içerir(2)}"
            """);
        Assert.Equal(["10", "doğru", "yanlış"], result.Output.Lines);
    }

    [Fact]
    public void ArrayUsesLengthAndIndexOutOfRangeIsStructured()
    {
        var length = Run.Clean("""
            sayılar: dizi<tamsayı> = [4, 5]
            syaz "{sayılar.uzunluk}"
            """);
        Assert.Equal("2", Assert.Single(length.Output.Lines));
        var range = Run.Text("""
            sayılar: liste<tamsayı> = [1]
            syaz "{sayılar[3]}"
            """);
        Assert.True(range.Evaluated);
        Assert.Contains(range.Diagnostics, item => item.Code == UsRuntimeCodes.IndexRange);
    }
}

public class FilterSortTests
{
    [Fact]
    public void FilterKeepsActiveAdultsAndDoesNotMutateTheSource()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
                aktif: mantıksal
            }
            kullanıcılar: liste<Kullanıcı> = [
                @Kullanıcı { ad = "Ali" yaş = 20 aktif = doğru },
                @Kullanıcı { ad = "Ayşe" yaş = 17 aktif = doğru },
                @Kullanıcı { ad = "Mehmet" yaş = 35 aktif = yanlış }
            ]
            aktifler: liste<Kullanıcı> = kullanıcılar.filtrele(^.aktif && ^.yaş >= 18)
            syaz "{aktifler.adet}"
            syaz "{kullanıcılar.adet}"
            """);
        Assert.Equal(["1", "3"], result.Output.Lines);
    }

    [Fact]
    public void EqualKeysKeepSourceOrder()
    {
        var result = Run.Clean("""
            model Satır
            {
                ad: metin
                puan: tamsayı
            }
            satırlar: liste<Satır> = [
                @Satır { ad = "A" puan = 1 },
                @Satır { ad = "B" puan = 1 }
            ]
            sıralı: liste<Satır> = satırlar.sırala(^.puan)
            dön sıralı -> satır
            {
                syaz satır.ad
            }
            """);
        Assert.Equal(["A", "B"], result.Output.Lines);
    }

    [Fact]
    public void DescendingSortsTextByOrdinal()
    {
        var result = Run.Clean("""
            adlar: liste<metin> = ["Ayşe", "Ali"]
            sıralı: liste<metin> = adlar.tsırala()
            syaz sıralı[0]
            """);
        Assert.Equal("Ayşe", Assert.Single(result.Output.Lines));
    }
}

public class QuantifierTests
{
    [Fact]
    public void EmptyFirstOrNullIsNullAndEmptyAllIsTrue()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            kullanıcılar: liste<Kullanıcı> = []
            k: Kullanıcı? = kullanıcılar.ilkYadaBoş()
            ad: metin? = k?.ad
            syaz "{ad}"
            syaz "{kullanıcılar.tümü(^.ad == "x")}"
            """);
        Assert.Equal(["", "doğru"], result.Output.Lines);
    }

    [Fact]
    public void FirstOnEmptyIsUs1515AndAnyStopsEarly()
    {
        var empty = Run.Text("""
            sayılar: liste<tamsayı> = []
            sayılar.ilk()
            """);
        Assert.True(empty.Evaluated);
        Assert.Contains(empty.Diagnostics, item => item.Code == UsRuntimeCodes.EmptyFirst);
        var any = Run.Clean("""
            sayaç: tamsayı = 0
            fonksiyon izle(sayı: tamsayı): mantıksal
            {
                sayaç++
                => sayı == 1
            }
            sayılar: liste<tamsayı> = [1, 2, 3]
            bulundu: mantıksal = sayılar.varMı(izle(^))
            syaz "{sayaç}"
            syaz "{bulundu}"
            """);
        Assert.Equal(["1", "doğru"], any.Output.Lines);
    }
}

public class AggregateTests
{
    [Fact]
    public void AverageOfThreeIsTwo()
    {
        var result = Run.Clean("""
            sayılar: liste<tamsayı> = [1, 2, 3]
            syaz "{sayılar.ortalama()}"
            syaz "{sayılar.toplam()}"
            syaz "{sayılar.min()}"
            syaz "{sayılar.maks()}"
            syaz "{sayılar.adet(^ > 1)}"
            """);
        Assert.Equal(["2", "6", "1", "3", "2"], result.Output.Lines);
    }

    [Fact]
    public void EmptyMinIsUs1516()
    {
        var result = Run.Text("""
            sayılar: liste<tamsayı> = []
            sayılar.min()
            """);
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.EmptyExtreme);
    }
}

public class NestedCaretEvalTests
{
    [Fact]
    public void InnerCaretIsTheInnerItemAndOuterParameterRemains()
    {
        var result = Run.Clean("""
            gruplar: liste<liste<tamsayı>> = [[1, 2], [3]]
            eşleşen: liste<liste<tamsayı>> = gruplar.filtrele((dış: liste<tamsayı>) => dış.filtrele(^ == 1).adet == 1)
            syaz "{eşleşen.adet}"
            sayılar: liste<tamsayı> = [1, 4]
            iç: liste<tamsayı> = [1]
            kalan: liste<tamsayı> = sayılar.filtrele(iç.filtrele(^ == 1).adet == 1 && ^ > 2)
            syaz "{kalan.ilk()}"
            """);
        Assert.Equal(["1", "4"], result.Output.Lines);
    }
}

public class MappingEvalTests
{
    [Fact]
    public void NestedQueryRestoresTheMappingSource()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            model Dto
            {
                ad: metin
                n: tamsayı
            }
            sayılar: liste<tamsayı> = [1]
            dönüşüm Kullanıcı => Dto
            {
                n = sayılar.filtrele(^ == 1).adet
                ad = ^.ad
            }
            kullanıcı: Kullanıcı = @Kullanıcı { ad = "İbrahim" }
            dto: Dto = kullanıcı.dönüştür<Dto>()
            syaz dto.ad
            syaz "{dto.n}"
            """);
        Assert.Equal(["İbrahim", "1"], result.Output.Lines);
    }

    [Fact]
    public void ExplicitMappingCopiesDeclaredFields()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
            }
            model KullanıcıDto
            {
                ad: metin
            }
            dönüşüm Kullanıcı => KullanıcıDto
            {
                ad = ^.ad
            }
            kullanıcı: Kullanıcı = @Kullanıcı { ad = "İbrahim" yaş = 38 }
            dto: KullanıcıDto = kullanıcı.dönüştür<KullanıcıDto>()
            syaz dto.ad
            """);
        Assert.Equal("İbrahim", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void SecondMappingIsUs0850()
    {
        var result = Run.Text("""
            model Kullanıcı
            {
                ad: metin
            }
            model KullanıcıDto
            {
                ad: metin
            }
            dönüşüm Kullanıcı => KullanıcıDto
            {
                ad = ^.ad
            }
            dönüşüm Kullanıcı => KullanıcıDto
            {
                ad = ^.ad
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.DuplicateMapping);
    }
}

public class NoMagicMappingTests
{
    [Fact]
    public void MissingMappingIsUs0851AndPartialCopyWritesOnlyTheDeclaredField()
    {
        var missing = Run.Text("""
            model Kullanıcı
            {
                ad: metin
            }
            model KullanıcıDto
            {
                ad: metin
            }
            kullanıcı: Kullanıcı = @Kullanıcı { ad = "İbrahim" }
            kullanıcı.dönüştür<KullanıcıDto>()
            """);
        Assert.False(missing.Evaluated);
        Assert.Contains(missing.Diagnostics, item => item.Code == DiagnosticCodes.MissingMapping);
        Assert.Empty(missing.Output.Lines);
        var partial = Run.Clean("""
            model Kaynak
            {
                ad: metin
                kod: tamsayı
            }
            model Hedef
            {
                ad: metin
                kod: tamsayı?
            }
            dönüşüm Kaynak => Hedef
            {
                ad = ^.ad
            }
            kaynak: Kaynak = @Kaynak { ad = "Ali" kod = 7 }
            hedef: Hedef = kaynak.dönüştür<Hedef>()
            syaz hedef.ad
            syaz "{hedef.kod}"
            """);
        Assert.Equal(["Ali", ""], partial.Output.Lines);
    }
}

public class WindowQueryTests
{
    [Fact]
    public void SkipAndLimitKeepOrderAndModelsCompareByFields()
    {
        var window = Run.Clean("""
            sayılar: liste<tamsayı> = [1, 2, 3, 4]
            pencere: liste<tamsayı> = sayılar.atla(1).sınırla(2)
            syaz "{pencere.adet}"
            syaz "{pencere[0]}"
            syaz "{pencere[1]}"
            """);
        Assert.Equal(["2", "2", "3"], window.Output.Lines);
        var distinct = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
            }
            a: Kullanıcı = @Kullanıcı { ad = "Ali" }
            b: Kullanıcı = @Kullanıcı { ad = "Ali" }
            kişiler: liste<Kullanıcı> = [a, b]
            tek: liste<Kullanıcı> = kişiler.benzersiz()
            syaz "{tek.adet}"
            """);
        Assert.Equal("1", Assert.Single(distinct.Output.Lines));
    }

    [Fact]
    public void NegativeWindowIsUs0849()
    {
        var result = Run.Text("""
            sayılar: liste<tamsayı> = [1]
            sayılar.atla(-1)
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.NegativeWindow);
    }
}

public class AdultQueryEndToEndTests
{
    [Fact]
    public void AdultsArePrintedAndCaretIsBound()
    {
        var text = """
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
            }
            kullanıcılar: liste<Kullanıcı> = [
                @Kullanıcı { ad = "Ali" yaş = 20 },
                @Kullanıcı { ad = "Ayşe" yaş = 17 }
            ]
            yetişkinler: liste<Kullanıcı> = kullanıcılar.filtrele(^.yaş >= 18)
            dön yetişkinler -> kullanıcı
            {
                syaz kullanıcı.ad
            }
            """;
        var result = Run.Clean(text);
        Assert.Equal("Ali", Assert.Single(result.Output.Lines));
        var sample = Path.Combine(FindRoot(), "samples", "Koleksiyonlar", "ana.us");
        var sampleResult = ProgramRunner.Run(SourceFile.FromText(sample, File.ReadAllText(sample)));
        Assert.True(sampleResult.Evaluated);
        Assert.Empty(sampleResult.Diagnostics);
        Assert.Equal(["Ali", "Mehmet"], sampleResult.Output.Lines);
        var file = SourceFile.FromText("deneme.us", text);
        var bound = Binder.Bind(file, global::Us.Parser.Parser.Parse(file).Root);
        Assert.Contains(bound.Members, member => HasCaret(member));
    }

    private static bool HasCaret(BoundNode node) =>
        node.Operator == TokenKind.Caret || node.Children.Any(HasCaret);

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

public class QueryDiagnosticTests
{
    [Fact]
    public void FilterOnNumberDoesNotRun()
    {
        var result = Run.Text("""
            sayı: tamsayı = 1
            sayı.filtrele(doğru)
            syaz "çalışmamalı"
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.QueryTarget);
        Assert.Empty(result.Output.Lines);
    }

    [Fact]
    public void SumOfTextIsRejected()
    {
        var result = Run.Text("""
            adlar: liste<metin> = ["Ali"]
            adlar.toplam()
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.QuerySelector);
    }
}

public class QueryIntegrationTests
{
    [Fact]
    public void FilterCanCallAFunctionAndVirtualDispatchRemains()
    {
        var result = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
            }
            fonksiyon yetişkinMi(k: Kullanıcı): mantıksal
            {
                => k.yaş >= 18
            }
            sınıf Hayvan
            {
                genel sanal fonksiyon ses(): metin
                {
                    => "Hayvan"
                }
            }
            sınıf Köpek : Hayvan
            {
                genel yerinegeç fonksiyon ses(): metin
                {
                    => "Köpek"
                }
            }
            kullanıcılar: liste<Kullanıcı> = [
                @Kullanıcı { ad = "Ali" yaş = 20 },
                @Kullanıcı { ad = "Ayşe" yaş = 17 }
            ]
            yetişkinler: liste<Kullanıcı> = kullanıcılar.filtrele(yetişkinMi(^))
            hayvanlar: liste<Hayvan> = [@Köpek()]
            syaz "{yetişkinler.adet}"
            syaz hayvanlar.ilk().ses()
            """);
        Assert.Equal(["1", "Köpek"], result.Output.Lines);
    }

    [Fact]
    public void CountLoopAndChainMatchTheMilestonePrograms()
    {
        var count = Run.Clean("""
            sayılar: liste<tamsayı> = [1, 2, 3, 4, 5]
            syaz "{sayılar.adet}"
            """);
        Assert.Equal("5", Assert.Single(count.Output.Lines));
        var loop = Run.Clean("""
            sayılar: liste<tamsayı> = [10, 20, 30]
            toplam: tamsayı = 0
            dön sayılar -> sayı
            {
                toplam += sayı
            }
            syaz "{toplam}"
            """);
        Assert.Equal("60", Assert.Single(loop.Output.Lines));
        var chain = Run.Clean("""
            model Kullanıcı
            {
                ad: metin
                yaş: tamsayı
                aktif: mantıksal
                puan: tamsayı
            }
            kullanıcılar: liste<Kullanıcı> = [
                @Kullanıcı { ad = "Ali" yaş = 20 aktif = doğru puan = 1 },
                @Kullanıcı { ad = "Ayşe" yaş = 17 aktif = doğru puan = 9 },
                @Kullanıcı { ad = "Mehmet" yaş = 35 aktif = yanlış puan = 4 },
                @Kullanıcı { ad = "Can" yaş = 40 aktif = doğru puan = 8 }
            ]
            sonuç: liste<Kullanıcı> = kullanıcılar.filtrele(^.aktif && ^.yaş >= 18).tsırala(^.puan).atla(0).sınırla(10)
            syaz "{sonuç.adet}"
            syaz sonuç[0].ad
            """);
        Assert.Equal(["2", "Can"], chain.Output.Lines);
    }

    [Fact]
    public void ModelCopyStillSharesTheList()
    {
        var result = Run.Clean("""
            model Kutu
            {
                sayılar: liste<tamsayı>
            }
            kaynak: Kutu = @Kutu { sayılar = [1] }
            kopya: Kutu = kaynak ile { }
            kopya.sayılar.ekle(2)
            syaz "{kaynak.sayılar.adet}"
            """);
        Assert.Equal("2", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void SelectorRunsOncePerItemAndPredicateShortCircuits()
    {
        var result = Run.Clean("""
            model Satır
            {
                puan: tamsayı
                aktif: mantıksal
            }
            sayaç: tamsayı = 0
            fonksiyon anahtar(satır: Satır): tamsayı
            {
                sayaç++
                => satır.puan
            }
            fonksiyon iz(): tamsayı
            {
                sayaç++
                => 1
            }
            satırlar: liste<Satır> = [
                @Satır { puan = 2 aktif = yanlış },
                @Satır { puan = 1 aktif = doğru }
            ]
            sıralı: liste<Satır> = satırlar.sırala(anahtar(^))
            syaz "{sayaç}"
            sayaç = 0
            satırlar.filtrele(^.aktif && iz() == 1)
            syaz "{sayaç}"
            """);
        Assert.Equal(["2", "1"], result.Output.Lines);
    }

    [Fact]
    public void MutationDuringIterationIsUs1519()
    {
        var result = Run.Text("""
            sayılar: liste<tamsayı> = [1, 2]
            dön sayılar -> sayı
            {
                sayılar.ekle(sayı)
            }
            """);
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.CollectionModified);
    }
}
