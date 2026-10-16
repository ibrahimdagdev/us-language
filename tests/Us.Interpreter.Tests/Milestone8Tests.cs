using Us.Runtime;

namespace Us.Interpreter.Tests;

public sealed class RecordingScheduler : IUsScheduler
{
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _second = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<long> Requests { get; } = [];

    public int Started { get; private set; }

    public Task FirstStarted => _first.Task;

    public Task SecondStarted => _second.Task;

    public ValueTask DelayAsync(long milliseconds)
    {
        Requests.Add(milliseconds);
        Started++;
        if (Started == 1)
        {
            _first.TrySetResult(true);
        }

        if (Started == 2)
        {
            _second.TrySetResult(true);
        }

        return new ValueTask(_release.Task);
    }

    public void Release() => _release.TrySetResult(true);
}

public class TryEvalTests
{
    [Fact]
    public void CatchContinuesAndFinallyRuns()
    {
        var result = Run.Clean("""
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
        Assert.Equal(["Yakalandı", "1"], result.Output.Lines);
    }

    [Fact]
    public void FinallyRunsAfterReturnAndBreak()
    {
        var result = Run.Clean("""
            fonksiyon x(): tamsayı
            {
                dene
                {
                    => 10
                }
                sonunda
                {
                    syaz "temizle"
                }
            }
            syaz "{x()}"
            dön 1 ile 1 -> i
            {
                dene
                {
                    çık
                }
                sonunda
                {
                    syaz "son"
                }
            }
            syaz "bitti"
            """);
        Assert.Equal(["temizle", "10", "son", "bitti"], result.Output.Lines);
    }

    [Fact]
    public void UntypedCatchSeesRuntimeFaultAndTypedCatchDoesNot()
    {
        var caught = Run.Clean("""
            dene
            {
                sayı: tamsayı = 1 / 0
            }
            yakala hata
            {
                syaz "yakalandı"
            }
            """);
        Assert.Equal("yakalandı", Assert.Single(caught.Output.Lines));
        var typed = Run.Text("""
            dene
            {
                sayı: tamsayı = 1 / 0
            }
            yakala hata: Hata
            {
                syaz "yok"
            }
            """);
        Assert.True(typed.Evaluated);
        Assert.Contains(typed.Diagnostics, item => item.Code == UsRuntimeCodes.DivisionByZero);
        Assert.Empty(typed.Output.Lines);
    }

    [Fact]
    public void MutationAndRefStayAfterTheError()
    {
        var result = Run.Clean("""
            model Kutu
            {
                n: tamsayı
            }
            fonksiyon değiştir(ref sayı: tamsayı)
            {
                sayı = 5
                fırlat @Hata("x")
            }
            kutu: Kutu = @Kutu { n = 1 }
            sayı: tamsayı = 1
            dene
            {
                kutu.n = 9
                değiştir(ref sayı)
            }
            yakala hata
            {
            }
            syaz "{kutu.n}"
            syaz "{sayı}"
            """);
        Assert.Equal(["9", "5"], result.Output.Lines);
    }
}

public class TypedCatchTests
{
    [Fact]
    public void FirstMatchingArmRunsAndSubclassMatchesTheBase()
    {
        var result = Run.Clean("""
            sınıf VeritabanıHatası : Hata
            {
            }
            sınıf Diğer : Hata
            {
            }
            dene
            {
                fırlat @VeritabanıHatası { mesaj = "db" }
            }
            yakala hata: Diğer
            {
                syaz "yanlış"
            }
            yakala hata: VeritabanıHatası
            {
                syaz hata.mesaj
            }
            dene
            {
                fırlat @VeritabanıHatası { mesaj = "üst" }
            }
            yakala hata: Hata
            {
                syaz hata.mesaj
            }
            """);
        Assert.Equal(["db", "üst"], result.Output.Lines);
    }
}

public class BasindaEvalTests
{
    [Fact]
    public void PreparationErrorSkipsTheBodyAndStillCleansUp()
    {
        var result = Run.Clean("""
            deneSayacı: tamsayı = 0
            sayaç: tamsayı = 0
            başında
            {
                fırlat @Hata("hazır")
            }
            dene
            {
                deneSayacı = 1
            }
            yakala hata
            {
                sayaç = 1
            }
            sonunda
            {
                sayaç = sayaç + 1
            }
            syaz "{deneSayacı}"
            syaz "{sayaç}"
            """);
        Assert.Equal(["0", "2"], result.Output.Lines);
    }
}

public class AltBasindaEvalTests
{
    [Fact]
    public void UnderscorePreparationPropagatesAfterFinally()
    {
        var result = Run.Clean("""
            fonksiyon çalış()
            {
                _başında
                {
                    fırlat @Hata("hazır")
                }
                dene
                {
                    syaz "dene"
                }
                yakala hata
                {
                    syaz "yakala"
                }
                sonunda
                {
                    syaz "son"
                }
            }
            dene
            {
                çalış()
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            """);
        Assert.Equal(["son", "hazır"], result.Output.Lines);
    }

    [Fact]
    public void CallerSeesTheUsCallChain()
    {
        var result = Run.Text("""
            fonksiyon c()
            {
                fırlat @Hata("x")
            }
            fonksiyon b()
            {
                c()
            }
            fonksiyon a()
            {
                b()
            }
            a()
            """);
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.UserThrown && item.Message.Contains("a → b → c", StringComparison.Ordinal));
    }
}

public class FinallyPriorityTests
{
    [Fact]
    public void CleanupErrorReplacesTheBodyError()
    {
        var result = Run.Clean("""
            dene
            {
                dene
                {
                    fırlat @Hata("gövde")
                }
                yakala hata
                {
                }
                sonunda
                {
                    fırlat @Hata("temiz")
                }
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            """);
        Assert.Equal("temiz", Assert.Single(result.Output.Lines));
    }
}

public class AwaitEvalTests
{
    [Fact]
    public void AwaitReturnsTheBoundValueAndRejectsAPlainFunction()
    {
        var result = Run.Clean("""
            afonksiyon değerGetir(): tamsayı
            {
                => 42
            }
            sonuç: tamsayı = bekle değerGetir()
            syaz "{sonuç}"
            """);
        Assert.Equal("42", Assert.Single(result.Output.Lines));
        var plain = Run.Text("""
            fonksiyon g(): tamsayı
            {
                => 1
            }
            fonksiyon f(): tamsayı
            {
                bekle g()
                => 1
            }
            """);
        Assert.False(plain.Evaluated);
        Assert.Contains(plain.Diagnostics, item => item.Code == "US0835");
    }

    [Fact]
    public async Task SuspensionKeepsTheLocalFrame()
    {
        var scheduler = new RecordingScheduler();
        var running = ProgramRunner.RunTextAsync("deneme.us", """
            afonksiyon iç(): tamsayı
            {
                bekle 5.milisaniye
                => 7
            }
            afonksiyon dış(): tamsayı
            {
                x: tamsayı = 1
                y: tamsayı = bekle iç()
                => x + y
            }
            syaz "{bekle dış()}"
            """, scheduler);
        var started = await Task.WhenAny(scheduler.FirstStarted, running.AsTask());
        Assert.Same(scheduler.FirstStarted, started);
        Assert.False(running.IsCompleted);
        scheduler.Release();
        var result = await running;
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("8", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public async Task AsyncErrorStaysASingleUsError()
    {
        var result = await ProgramRunner.RunTextAsync("deneme.us", """
            afonksiyon pat(): tamsayı
            {
                fırlat @Hata("x")
                => 0
            }
            dene
            {
                bekle pat()
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            """);
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("x", Assert.Single(result.Output.Lines));
    }
}

public class AsyncSampleTests
{
    [Fact]
    public async Task SampleRecordsOneHundredMillisecondsAndPrintsTamam()
    {
        var scheduler = new RecordingScheduler();
        var path = Path.Combine(FindRoot(), "samples", "Async", "ana.us");
        var running = ProgramRunner.RunAsync(Us.Core.SourceFile.FromText(path, File.ReadAllText(path)), scheduler);
        var started = await Task.WhenAny(scheduler.FirstStarted, running.AsTask());
        Assert.Same(scheduler.FirstStarted, started);
        scheduler.Release();
        var result = await running;
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Tamam", Assert.Single(result.Output.Lines));
        Assert.Equal(100, Assert.Single(scheduler.Requests));
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

public class ConcurrentStartTests
{
    [Fact]
    public async Task IndependentWorkStartsTogetherAndDependentWorkWaits()
    {
        var together = new RecordingScheduler();
        var running = ProgramRunner.RunTextAsync("deneme.us", """
            afonksiyon bir(): tamsayı
            {
                bekle 1.milisaniye
                => 4
            }
            a: tamsayı = 0
            b: tamsayı = 0
            eşişlem
            {
                a = bekle bir()
                b = bekle bir()
            }
            syaz "{a + b}"
            """, together);
        var started = await Task.WhenAny(together.SecondStarted, running.AsTask());
        Assert.Same(together.SecondStarted, started);
        Assert.False(running.IsCompleted);
        together.Release();
        var done = await running;
        Assert.True(done.Evaluated, string.Join(Environment.NewLine, done.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal("8", Assert.Single(done.Output.Lines));

        var dependent = new RecordingScheduler();
        var second = ProgramRunner.RunTextAsync("deneme.us", """
            afonksiyon bir(): tamsayı
            {
                bekle 1.milisaniye
                => 1
            }
            afonksiyon iki(n: tamsayı): tamsayı
            {
                bekle 1.milisaniye
                => n
            }
            a: tamsayı = 0
            b: tamsayı = 0
            eşişlem
            {
                a = bekle bir()
                b = bekle iki(a)
            }
            syaz "{b}"
            """, dependent);
        var first = await Task.WhenAny(dependent.FirstStarted, second.AsTask());
        Assert.Same(dependent.FirstStarted, first);
        var raced = await Task.WhenAny(dependent.SecondStarted, Task.Delay(200));
        Assert.Equal(1, dependent.Started);
        Assert.NotSame(dependent.SecondStarted, raced);
        dependent.Release();
        var result = await second;
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal("1", Assert.Single(result.Output.Lines));
    }
}

public class ConcurrentErrorTests
{
    [Fact]
    public void BothErrorsAreKeptAndAssignmentsAreRestored()
    {
        var result = Run.Clean("""
            afonksiyon kötü(mesaj: metin): tamsayı
            {
                fırlat @Hata(mesaj)
                => 0
            }
            değer: tamsayı = 1
            dene
            {
                eşişlem
                {
                    değer = bekle kötü("bir")
                    bekle kötü("iki")
                }
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            sonunda
            {
                syaz "son"
            }
            syaz "{değer}"
            """);
        Assert.Equal(["bir | iki", "son", "1"], result.Output.Lines);
    }
}

public class EventEvalTests
{
    [Fact]
    public void HandlersRunInOrderAndInstancesStaySeparate()
    {
        var result = Run.Clean("""
            sınıf Kutu
            {
                genel olay değişti: metin
                genel n: tamsayı
                genel fonksiyon bir(değer: metin)
                {
                    syaz "1"
                    n = n + 1
                }
                genel fonksiyon iki(değer: metin)
                {
                    syaz "2"
                }
                genel fonksiyon bağla()
                {
                    değişti += bir
                    değişti += iki
                }
            }
            kutu: Kutu = @Kutu()
            diğer: Kutu = @Kutu()
            kutu.bağla()
            kutu.değişti.tetikle("a")
            diğer.değişti.tetikle("b")
            syaz "{kutu.n}"
            kutu.değişti -= kutu.bir
            kutu.değişti.tetikle("c")
            """);
        Assert.Equal(["1", "2", "1", "2"], result.Output.Lines);
    }

    [Fact]
    public void MissingRemovalIsUs1520AndHandlerErrorsContinue()
    {
        var missing = Run.Text("""
            sınıf Kutu
            {
                genel olay değişti: metin
                genel fonksiyon bir(değer: metin)
                {
                }
            }
            kutu: Kutu = @Kutu()
            kutu.değişti -= kutu.bir
            """);
        Assert.True(missing.Evaluated);
        Assert.Contains(missing.Diagnostics, item => item.Code == UsRuntimeCodes.MissingSubscription);
        var errors = Run.Clean("""
            sınıf Kutu
            {
                genel olay değişti: metin
                genel fonksiyon pat(değer: metin)
                {
                    fırlat @Hata(değer)
                }
                genel fonksiyon iki(değer: metin)
                {
                    syaz "2"
                }
            }
            sınıf Taban
            {
                genel olay oldu: metin
                genel sanal fonksiyon ses(değer: metin)
                {
                    syaz "taban"
                }
            }
            sınıf Türev : Taban
            {
                genel yerinegeç fonksiyon ses(değer: metin)
                {
                    syaz "türev"
                }
            }
            kutu: Kutu = @Kutu()
            kutu.değişti += kutu.pat
            kutu.değişti += kutu.iki
            dene
            {
                kutu.değişti.tetikle("x")
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            türev: Taban = @Türev()
            türev.oldu += türev.ses
            türev.oldu.tetikle("z")
            """);
        Assert.Equal(["2", "x", "türev"], errors.Output.Lines);
    }
}
