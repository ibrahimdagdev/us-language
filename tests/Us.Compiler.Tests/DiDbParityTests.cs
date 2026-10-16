using System.Collections.Immutable;
using Us.Core;

namespace Us.Compiler.Tests;

public class DiDbParityTests
{
    [Fact]
    public void TransientBindResolvesAndAtDoesNotUseContainer()
    {
        PhaseAParity.AssertParity("""
            servis Kutu : geçici
            {
                genel n: tamsayı
                genel oluştur() { n = 7 }
            }
            a: Kutu = @Kutu()
            bağla servis: Kutu
            syaz "{a.n}"
            syaz "{servis.n}"
            """);
    }

    [Fact]
    public void SingletonKeepsMutations()
    {
        PhaseAParity.AssertParity("""
            servis Kutu : tekil
            {
                genel n: tamsayı
                genel oluştur() { n = 1 }
            }
            bağla servis: Kutu
            servis.n = 9
            syaz "{servis.n}"
            """);
    }

    [Fact]
    public void ScopedResolvesWithinProgramScope()
    {
        PhaseAParity.AssertParity("""
            servis Kutu : kapsamlı
            {
                genel n: tamsayı
                genel oluştur() { n = 3 }
            }
            bağla servis: Kutu
            syaz "{servis.n}"
            """);
    }

    [Fact]
    public void ConstructorDependencyChainMatches()
    {
        PhaseAParity.AssertParity("""
            servis C : geçici
            {
                genel n: tamsayı
                genel oluştur() { n = 3 }
            }
            servis B : geçici
            {
                genel c: C
                oluştur(bağımlı: C) { c = bağımlı }
            }
            servis A : geçici
            {
                genel b: B
                oluştur(bağımlı: B) { b = bağımlı }
            }
            bağla servis: A
            syaz "{servis.b.c.n}"
            """);
    }

    [Fact]
    public void ServiceCycleReportsUs1546StyleMessage()
    {
        PhaseAParity.AssertParity("""
            servis A : geçici
            {
                oluştur(b: B) { }
            }
            servis B : geçici
            {
                oluştur(a: A) { }
            }
            dene
            {
                bağla servis: A
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            """);
    }

    [Fact]
    public void MemoryDatabaseInsertAndQueryMatches()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ayşe" aktif = doğru })
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
    }

    [Fact]
    public void TransactionAutoCommitPersists()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
    }

    [Fact]
    public void ExplicitOnaylaPersists()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                onayla
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
    }

    [Fact]
    public void GerialDropsRowAndBodyMayContinue()
    {
        // M12: after gerial, body may continue; later writes persist outside the closed tx.
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                gerial
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ayşe" aktif = doğru })
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
    }

    [Fact]
    public void UserErrorRollsBackTransaction()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            dene
            {
                işlem
                {
                    bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                    fırlat @Hata("x")
                }
            }
            yakala hata: Hata
            {
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
    }

    [Fact]
    public void TransactionWithoutDatabaseStillRuns()
    {
        PhaseAParity.AssertParity("""
            işlem
            {
                onayla
            }
            syaz "bitti"
            """);
    }

    [Fact]
    public void FilterSortAndMaterializeMatch()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Şeyma" aktif = doğru })
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Zeynep" aktif = yanlış })
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.aktif).sırala(^.ad).tümü()
            syaz "{satırlar.adet}"
            syaz "{satırlar.ilk.ad}"
            syaz "{satırlar.filtrele(^.ad == "Şeyma").adet}"
            """);
    }

    [Fact]
    public void FilterNoMatchAndTextEqualityMatch()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ayşe" aktif = doğru })
            boş: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.ad == "Yok").tümü()
            syaz "{boş.adet}"
            bulunan: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.ad == "Ali").tümü()
            syaz "{bulunan.adet}"
            syaz "{bulunan.ilk.ad}"
            """);
    }

    [Fact]
    public void NumericComparisonAndDescendingSortMatch()
    {
        PhaseAParity.AssertParity("""
            model Ürün
            {
                id: tamsayı : anahtar
                ad: metin
                fiyat: tamsayı
            }
            veritabanı UygulamaDb
            {
                tablo ürünler: Ürün
            }
            bağla db: UygulamaDb
            bekle db.ürünler.ekle(@Ürün { ad = "a" fiyat = 10 })
            bekle db.ürünler.ekle(@Ürün { ad = "b" fiyat = 30 })
            bekle db.ürünler.ekle(@Ürün { ad = "c" fiyat = 20 })
            satırlar: liste<Ürün> = bekle db.ürünler.filtrele(^.fiyat > 15).tsırala(^.fiyat).tümü()
            syaz "{satırlar.adet}"
            syaz "{satırlar.ilk.ad}:{satırlar.ilk.fiyat}"
            """);
    }

    [Fact]
    public void UpdateAndDeleteMatch()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            a: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            a.ad = "Veli"
            bekle db.kullanıcılar.güncelle(a)
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.ilk.ad}"
            bekle db.kullanıcılar.sil(a)
            kalan: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{kalan.adet}"
            """);
    }

    [Fact]
    public void FlatBirlikteIncludeMatches()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            model Sipariş
            {
                id: tamsayı : anahtar
                kullanıcı: Kullanıcı
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
                tablo siparişler: Sipariş
            }
            bağla db: UygulamaDb
            kişi: Kullanıcı = @Kullanıcı { ad = "Ada" }
            bekle db.kullanıcılar.ekle(kişi)
            sipariş: Sipariş = @Sipariş { kullanıcı = kişi }
            bekle db.siparişler.ekle(sipariş)
            satırlar: liste<Sipariş> = bekle db.siparişler.birlikte(^.kullanıcı).tümü()
            syaz "{satırlar.adet}"
            syaz "{satırlar.ilk.kullanıcı.ad}"
            """);
    }

    [Fact]
    public void BirlikteMultipleParentsKeepChildrenIsolated()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            model Sipariş
            {
                id: tamsayı : anahtar
                kullanıcı: Kullanıcı
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
                tablo siparişler: Sipariş
            }
            bağla db: UygulamaDb
            ada: Kullanıcı = @Kullanıcı { ad = "Ada" }
            ali: Kullanıcı = @Kullanıcı { ad = "Ali" }
            bekle db.kullanıcılar.ekle(ada)
            bekle db.kullanıcılar.ekle(ali)
            bekle db.siparişler.ekle(@Sipariş { kullanıcı = ada })
            bekle db.siparişler.ekle(@Sipariş { kullanıcı = ali })
            satırlar: liste<Sipariş> = bekle db.siparişler.birlikte(^.kullanıcı).tümü()
            syaz "{satırlar.adet}"
            syaz "{satırlar.filtrele(^.id == 1).ilk.kullanıcı.ad}"
            syaz "{satırlar.filtrele(^.id == 2).ilk.kullanıcı.ad}"
            """);
    }

    [Fact]
    public void QueryInsideTransactionMatches()
    {
        PhaseAParity.AssertParity("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ayşe" aktif = yanlış })
                satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.aktif).tümü()
                syaz "{satırlar.adet}"
                onayla
            }
            """);
    }

    [Fact]
    public void NestedBirlikteRemainsUs0863()
    {
        var source = """
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            model Adres
            {
                id: tamsayı : anahtar
                şehir: metin
            }
            model Sipariş
            {
                id: tamsayı : anahtar
                kullanıcı: Kullanıcı
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
                tablo siparişler: Sipariş
            }
            bağla db: UygulamaDb
            bekle db.siparişler.birlikte(^.kullanıcı.adres).tümü()
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bound = Compiler.BindOnly([Us.Core.SourceFile.FromText("nested.us", source)]);
        Assert.True(bound.Diagnostics.HasErrors);
        Assert.Contains(
            bound.Diagnostics.ToImmutableArray(),
            item => item.Code == Us.Core.DiagnosticCodes.NestedInclude);
    }

    [Fact]
    public void InvalidIncludeMemberIsUs0862()
    {
        var source = """
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin
            }
            model Sipariş
            {
                id: tamsayı : anahtar
                kullanıcı: Kullanıcı
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
                tablo siparişler: Sipariş
            }
            bağla db: UygulamaDb
            bekle db.siparişler.birlikte(^.yok).tümü()
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bound = Compiler.BindOnly([Us.Core.SourceFile.FromText("include.us", source)]);
        Assert.True(bound.Diagnostics.HasErrors);
        Assert.Contains(
            bound.Diagnostics.ToImmutableArray(),
            item => item.Code == Us.Core.DiagnosticCodes.IncludeMember);
    }
}
