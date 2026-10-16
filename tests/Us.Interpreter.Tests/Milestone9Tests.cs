using Us.Core;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Interpreter.Tests;

public class TimeArithTests
{
    [Fact]
    public void FixedClockPlusDurationPrintsTheExpectedInstant()
    {
        var result = Milestone9Host.Host(Milestone9Host.Clock(), """
            son: tarihsaat = şimdi + 30.dakika
            fark: süre = son - şimdi
            toplam: süre = 30.dakika + 30.dakika
            syaz "{son}"
            syaz "{fark}"
            syaz "{toplam}"
            syaz "{şimdi < son}"
            """);
        Assert.Equal(["2026-10-07 00:30:00", "1800000 ms", "3600000 ms", "doğru"], result.Output.Lines);
    }

    [Fact]
    public void DurationPlusIntegerIsUs0807()
    {
        var result = Run.Text("süre: süre = 1.saniye + 1");
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.BinaryType);
    }
}

public class ClockTests
{
    [Fact]
    public void TwoReadsShareTheFixedClock()
    {
        var result = Milestone9Host.Host(Milestone9Host.Clock(), """
            syaz "{bugün}"
            syaz "{şimdi}"
            syaz "{şimdi}"
            """);
        Assert.Equal(["2026-10-07", "2026-10-07 00:00:00", "2026-10-07 00:00:00"], result.Output.Lines);
    }
}

public class TurkishStringTests
{
    [Fact]
    public void MembersUseTurkishCaseAndOrdinalSearch()
    {
        var result = Run.Clean("""
            syaz "  İbrahim  ".kırp()
            syaz "istanbul".büyük()
            syaz "İSTANBUL".küçük()
            syaz "I".küçük()
            syaz "i".büyük()
            içinde: mantıksal = "Merhaba dünya".içerir("dünya")
            baş: mantıksal = "Merhaba".başlar("Mer")
            son: mantıksal = "Merhaba".biter("aba")
            syaz "{içinde}"
            syaz "{baş}"
            syaz "{son}"
            syaz "Merhaba dünya".değiştir("dünya", "US")
            dön "a,b,c".böl(",") -> parça
            {
                syaz parça
            }
            syaz ",".birleştir(["a", "b", "c"])
            """);
        Assert.Equal(
            ["İbrahim", "İSTANBUL", "istanbul", "ı", "İ", "doğru", "doğru", "doğru", "Merhaba US", "a", "b", "c", "a,b,c"],
            result.Output.Lines);
    }
}

public class ConsoleBindTests
{
    [Fact]
    public void ClearEmptiesTheBufferAndSyazCannotBeRedefined()
    {
        var cleared = Run.Clean("""
            yaz "gizli"
            temizle
            syaz "açık"
            """);
        Assert.Equal("açık", Assert.Single(cleared.Output.Lines));
        Assert.Equal(string.Empty, cleared.Output.Pending);
        var duplicate = Run.Text("""
            fonksiyon syaz(değer: metin)
            {
            }
            """);
        Assert.Contains(duplicate.Diagnostics, item => item.Code == DiagnosticCodes.DuplicateName);
    }
}

public class InputEvalTests
{
    [Fact]
    public void AskWritesThePromptAndEmptyQueueIsUs1526()
    {
        var host = new UsHost { Input = new QueueInput(["İbrahim"]) };
        var result = ProgramRunner.RunText("deneme.us", """
            ad: metin = sor "Ad: "
            syaz ad
            """, host);
        Assert.Equal("Ad: İbrahim", Assert.Single(result.Output.Lines));
        var empty = ProgramRunner.RunText("deneme.us", "satır: metin = oku()", new UsHost());
        Assert.Contains(empty.Diagnostics, item => item.Code == UsRuntimeCodes.InputEnded);
    }
}

public class JsonStrictTests
{
    [Fact]
    public void UnknownFieldIsUs1527AndNullBecomesEmpty()
    {
        var unknown = Run.Text("""
            model Kutu { ad: metin }
            k: Kutu = json.oku<Kutu>("{{ \"ad\": \"A\", \"fazla\": 1 }}")
            """);
        Assert.Contains(unknown.Diagnostics, item => item.Code == UsRuntimeCodes.UnknownJsonField);
        var missing = Run.Text("""
            model Kutu { ad: metin : zorunlu }
            k: Kutu = json.oku<Kutu>("{{}}")
            """);
        Assert.Contains(missing.Diagnostics, item => item.Code == UsRuntimeCodes.MissingJsonField);
        var nullable = Run.Clean("""
            model Kutu { ad: metin? }
            k: Kutu = json.oku<Kutu>("{{ \"ad\": null }}")
            syaz "tamam"
            """);
        Assert.Equal("tamam", Assert.Single(nullable.Output.Lines));
        var precise = Run.Clean("""
            model Kutu { tutar: hondalık }
            k: Kutu = json.oku<Kutu>("{{ \"tutar\": 0.10 }}")
            syaz "{k.tutar}"
            """);
        Assert.Equal("0.10", Assert.Single(precise.Output.Lines));
        var broken = Run.Text("""
            model Kutu { ad: metin }
            k: Kutu = json.oku<Kutu>("[")
            """);
        Assert.Contains(broken.Diagnostics, item => item.Code == UsRuntimeCodes.JsonMalformed);
        var untyped = Run.Text("json.oku(\"[]\")");
        Assert.Contains(untyped.Diagnostics, item => item.Code == DiagnosticCodes.JsonTarget);
    }

    [Fact]
    public void NestedModelAndListRoundTrip()
    {
        var result = Run.Clean("""
            model Adres { şehir: metin }
            model Kişi { ad: metin adres: Adres }
            kişi: Kişi = @Kişi { ad = "İbrahim" adres = @Adres { şehir = "İstanbul" } }
            okunan: Kişi = json.oku<Kişi>(json.yaz(kişi))
            syaz okunan.adres.şehir
            kişiler: liste<Kişi> = json.oku<liste<Kişi>>("[{{ \"ad\": \"Ali\", \"adres\": {{ \"şehir\": \"Ankara\" }} }}]")
            syaz "{kişiler.adet}"
            """);
        Assert.Equal(["İstanbul", "1"], result.Output.Lines);
    }

    [Fact]
    public void DefaultFillsAMissingField()
    {
        var result = Run.Clean("""
            model Kutu { ad: metin : varsayılan("İbrahim") }
            k: Kutu = json.oku<Kutu>("{{}}")
            syaz k.ad
            """);
        Assert.Equal("İbrahim", Assert.Single(result.Output.Lines));
    }
}

public class ConfigOnceTests
{
    [Fact]
    public void SecondBindingDoesNotReadAgain()
    {
        var configuration = new MemoryConfiguration("""
            { "MongoDb": { "sunucu": "localhost", "port": 27017 } }
            """);
        var host = new UsHost { Configuration = configuration };
        var result = ProgramRunner.RunText("deneme.us", """
            model MongoDbAyar : Ayar("MongoDb")
            {
                sunucu: metin
                port: tamsayı
            }
            bağla ayar: MongoDbAyar
            bağla ayar: MongoDbAyar
            """, host);
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(1, configuration.Reads);
        Assert.Equal(2, host.Services.Count);
    }

    [Fact]
    public void MissingFileAndWrongLetterFail()
    {
        var missing = ProgramRunner.RunText("deneme.us", """
            model MongoDbAyar : Ayar("MongoDb") { sunucu: metin }
            bağla ayar: MongoDbAyar
            """, new UsHost { Configuration = new MemoryConfiguration(null) });
        Assert.Contains(missing.Diagnostics, item => item.Code == UsRuntimeCodes.MissingConfig);
        var section = ProgramRunner.RunText("deneme.us", """
            model MongoDbAyar : Ayar("MongoDb") { sunucu: metin }
            bağla ayar: MongoDbAyar
            """, new UsHost { Configuration = new MemoryConfiguration("{ \"Başka\": { \"sunucu\": \"x\" } }") });
        Assert.Contains(section.Diagnostics, item => item.Code == UsRuntimeCodes.MissingSection);
        var letter = ProgramRunner.RunText("deneme.us", """
            model MongoDbAyar : Ayar("MongoDb") { sunucu: metin }
            bağla ayar: MongoDbAyar
            """, new UsHost { Configuration = new MemoryConfiguration("{ \"MongoDb\": { \"Sunucu\": \"x\" } }") });
        Assert.Contains(letter.Diagnostics, item => item.Code == UsRuntimeCodes.UnknownJsonField);
    }
}

public class LogFacadeTests
{
    [Fact]
    public void FiveLevelsAreRecordedInOrder()
    {
        var host = new UsHost();
        var result = ProgramRunner.RunText("deneme.us", """
            log.iz("a")
            log.bilgi("Kullanıcı Ali")
            log.uyarı("c")
            log.hata("d")
            log.kritik("e")
            """, host);
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(
            [UsLogLevel.Trace, UsLogLevel.Information, UsLogLevel.Warning, UsLogLevel.Error, UsLogLevel.Critical],
            host.Log.Records.Select(record => record.Level));
        Assert.Equal("Kullanıcı Ali", host.Log.Records[1].Message);
    }
}

public class ValidationEvalTests
{
    [Fact]
    public void FailedRuleReturnsItsTextAndAValidUserReturnsNothing()
    {
        var failed = Run.Clean("""
            model Kullanıcı { yaş: tamsayı }
            kullanıcı: Kullanıcı = @Kullanıcı { yaş = 10 }
            doğrula kullanıcı
            {
                kural kullanıcı.yaş >= 18 => "18 yaşından küçük olamaz."
            }
            """);
        var messages = Assert.IsType<UsCollection>(failed.Value.Reference);
        Assert.Equal("18 yaşından küçük olamaz.", Assert.Single(messages.Items).Text);
        var valid = Run.Clean("""
            model Kullanıcı
            {
                yaş: tamsayı : aralık(18, 120)
                ad: metin : zorunlu, uzunluk(3)
            }
            kullanıcı: Kullanıcı = @Kullanıcı { yaş = 38 ad = "Ali" }
            doğrula kullanıcı
            {
                kural kullanıcı.yaş >= 18 => "18 yaşından küçük olamaz."
            }
            """);
        Assert.Empty(Assert.IsType<UsCollection>(valid.Value.Reference).Items);
        var range = Run.Clean("""
            model Kullanıcı { yaş: tamsayı : aralık(18, 120) }
            kullanıcı: Kullanıcı = @Kullanıcı { yaş = 10 }
            doğrula kullanıcı
            {
            }
            """);
        Assert.Contains(Assert.IsType<UsCollection>(range.Value.Reference).Items, item => item.Text == "'yaş' aralık dışındadır.");
    }
}

public class JsonFileSampleTests
{
    [Fact]
    public void SamplesRoundTripWithoutTouchingTheDisk()
    {
        var root = FindRoot();
        var json = ProgramRunner.Run(SourceFile.FromText("ana.us", File.ReadAllText(Path.Combine(root, "samples", "Json", "ana.us"))));
        Assert.Equal(["İbrahim", "38"], json.Output.Lines);
        var file = ProgramRunner.Run(SourceFile.FromText("ana.us", File.ReadAllText(Path.Combine(root, "samples", "Dosya", "ana.us"))));
        Assert.Equal("İbrahim", Assert.Single(file.Output.Lines));
    }

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

file static class Milestone9Host
{
    public static UsHost Clock() => new() { Clock = new FixedClock(new DateTime(2026, 10, 7)) };

    public static ExecutionResult Host(UsHost host, string text)
    {
        var result = ProgramRunner.RunText("deneme.us", text, host);
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Empty(result.Diagnostics);
        return result;
    }
}
