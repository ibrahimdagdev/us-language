using Us.Core;
using Us.Interpreter;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

public class ExpectedTests
{
    [Fact]
    public async Task TrueAssertionPassesAndFalseFailsWithSourceText()
    {
        var passed = await TestRunner.RunTextAsync("deneme.us", """
            test "toplam"
            {
                beklenen 2 + 2 == 4
            }
            """);
        Assert.Equal(1, passed.Passed);
        Assert.Equal(0, passed.Failed);

        var failed = await TestRunner.RunTextAsync("deneme.us", """
            test "fark"
            {
                beklenen 2 + 2 == 5
            }
            """);
        var test = Assert.Single(failed.Tests);
        Assert.Equal(TestStatus.Failed, test.Status);
        Assert.Equal(TestFailureKind.AssertionFailed, test.Failure!.Kind);
        Assert.Equal(DiagnosticCodes.AssertionFailed, test.Failure.Code);
        Assert.Contains("beklenen 2 + 2 == 5", test.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("Beklenen:\n5", test.Failure.Message, StringComparison.Ordinal);
        Assert.Contains("Gerçek:\n4", test.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpectedErrorPassesForThrowAndRuntimeFaultAndFailsWhenNothingIsThrown()
    {
        var thrown = await TestRunner.RunTextAsync("deneme.us", """
            test "hata"
            {
                beklenenHata
                {
                    fırlat @Hata("hata")
                }
            }
            """);
        Assert.Equal(TestStatus.Passed, Assert.Single(thrown.Tests).Status);

        var fault = await TestRunner.RunTextAsync("deneme.us", """
            test "bölme"
            {
                beklenenHata
                {
                    x: tamsayı = 1 / 0
                }
            }
            """);
        Assert.Equal(TestStatus.Passed, Assert.Single(fault.Tests).Status);

        var missing = await TestRunner.RunTextAsync("deneme.us", """
            test "yok"
            {
                beklenenHata
                {
                    x: tamsayı = 10
                }
            }
            """);
        Assert.Equal(TestFailureKind.ExpectedErrorMissing, Assert.Single(missing.Tests).Failure!.Kind);
        Assert.Equal(TestStatus.Failed, Assert.Single(missing.Tests).Status);
    }

    [Fact]
    public async Task AssertionExpressionErrorIsNotAFailureAndFinallyStillRuns()
    {
        var broken = await TestRunner.RunTextAsync("deneme.us", """
            test "ifade"
            {
                beklenen (1 / 0) == 0
            }
            """);
        var error = Assert.Single(broken.Tests);
        Assert.Equal(TestStatus.Error, error.Status);
        Assert.Equal(TestFailureKind.UnexpectedError, error.Failure!.Kind);
        Assert.Equal(UsRuntimeCodes.DivisionByZero, error.Failure.Code);

        var cleaned = await TestRunner.RunTextAsync("deneme.us", """
            test "temizlik"
            {
                beklenenHata
                {
                    dene
                    {
                        fırlat @Hata("x")
                    }
                    sonunda
                    {
                        syaz "temizlendi"
                    }
                }
            }
            """);
        var test = Assert.Single(cleaned.Tests);
        Assert.Equal(TestStatus.Passed, test.Status);
        Assert.Equal("temizlendi", Assert.Single(test.Output));
    }

    [Fact]
    public async Task PreparationErrorIsSeenByExpectedError()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "hazırlık"
            {
                beklenenHata
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
            }
            """);
        var test = Assert.Single(result.Tests);
        Assert.Equal(TestStatus.Passed, test.Status);
        Assert.Equal("son", Assert.Single(test.Output));
    }
}

public class AsyncTestTests
{
    [Fact]
    public async Task AwaitInsideTestAndExpectedErrorCompletes()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            afonksiyon getir(): tamsayı
            {
                bekle 0.milisaniye
                => 42
            }
            afonksiyon patla(): tamsayı
            {
                fırlat @Hata("x")
                => 0
            }
            test "veri gelir"
            {
                sonuç: tamsayı = bekle getir()
                beklenen sonuç == 42
            }
            test "async hata"
            {
                beklenenHata
                {
                    değer: tamsayı = bekle patla()
                }
            }
            """);
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(2, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Errors);
    }
}

public class TestRunnerTests
{
    [Fact]
    public async Task OneFailureDoesNotSkipTheOthers()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "geçer" { beklenen 1 == 1 }
            test "kalır" { beklenen 1 == 2 }
            test "yine" { beklenen 3 == 3 }
            """);
        Assert.Equal(3, result.Tests.Count);
        Assert.Equal(2, result.Passed);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Errors);
        Assert.Equal(["geçer", "kalır", "yine"], result.Tests.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task FilterRunsASubsetInSourceOrder()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "alfa" { }
            test "beta" { }
            test "gama" { }
            """, nameFilter: name => name is "gama" or "alfa");
        Assert.Equal(["alfa", "gama"], result.Tests.Select(item => item.Name).ToArray());
        Assert.Equal(2, result.Passed);
    }

    [Fact]
    public async Task EmptyAndTurkishNamesPassAndProgramRunnerSkipsTests()
    {
        var tests = await TestRunner.RunTextAsync("deneme.us", """
            syaz "program"
            test "kullanıcı doğru oluşturulur" { }
            """);
        Assert.Equal("kullanıcı doğru oluşturulur", Assert.Single(tests.Tests).Name);
        Assert.Equal(TestStatus.Passed, tests.Tests[0].Status);
        Assert.Equal("program", Assert.Single(tests.Tests[0].Output));

        var program = Run.Clean("""
            syaz "program"
            test "çalışmaz"
            {
                syaz "test"
            }
            """);
        Assert.Equal("program", Assert.Single(program.Output.Lines));
    }

    [Fact]
    public async Task CompilationErrorRunsNothing()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "geçer" { beklenen 1 == 1 }
            x: tamsayı = "metin"
            """);
        Assert.False(result.Compiled);
        Assert.Empty(result.Tests);
        Assert.NotEmpty(result.Diagnostics);
    }
}

public class TestDiagnosticFormatTests
{
    [Fact]
    public async Task FailureNamesTheTestAndPointsAtTheAssertionLine()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "fark"
            {
                beklenen 1 == 2
            }
            """);
        var failure = Assert.Single(result.Tests).Failure!;
        Assert.Contains("fark", failure.Message, StringComparison.Ordinal);
        Assert.Contains("beklenen 1 == 2", failure.Message, StringComparison.Ordinal);
        Assert.Equal(DiagnosticCodes.AssertionFailed, failure.Code);
        Assert.Equal(3, failure.Line);
    }
}

public class TestIsolationTests
{
    [Fact]
    public async Task GlobalsOutputFilesAndEventsStaySeparate()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            sayaç: tamsayı = 0
            sınıf Kutu
            {
                genel olay değişti: metin
                genel n: tamsayı = 0
                genel fonksiyon bir(değer: metin) { n = n + 1 }
                genel fonksiyon bağla() { değişti += bir }
                genel fonksiyon çalıştır() { değişti.tetikle("a") }
            }
            test "sayaç"
            {
                sayaç = sayaç + 1
                beklenen sayaç == 1
                syaz "A"
                dosya.yaz("x.txt", "a")
            }
            test "temiz"
            {
                beklenen sayaç == 0
                beklenen !dosya.varMı("x.txt")
                k: Kutu = @Kutu()
                k.bağla()
                k.çalıştır()
                beklenen k.n == 1
            }
            test "olay"
            {
                k: Kutu = @Kutu()
                k.çalıştır()
                beklenen k.n == 0
                syaz "B"
            }
            """);
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(3, result.Passed);
        Assert.Equal(["A"], result.Tests[0].Output);
        Assert.Empty(result.Tests[1].Output);
        Assert.Equal(["B"], result.Tests[2].Output);
    }

    [Fact]
    public async Task QueryAndModelEqualityUseLanguageEquality()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            model Kişi { ad: metin }
            test "sorgu"
            {
                sayılar: liste<tamsayı> = [1, 2, 3]
                bulunan: tamsayı = sayılar.filtrele(^ == 2).ilk()
                beklenen bulunan == 2
                a: Kişi = @Kişi { ad = "Ali" }
                b: Kişi = @Kişi { ad = "Ali" }
                beklenen a == b
            }
            """);
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.True(result.Tests[0].Status == TestStatus.Passed, result.Tests[0].Failure?.Message);
    }

    [Fact]
    public async Task LoopAssertionFailureKeepsItsOwnLine()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            test "döngü"
            {
                dön 1 ile 1 -> i
                {
                    beklenen i == 2
                }
            }
            """);
        var failure = Assert.Single(result.Tests).Failure!;
        Assert.Equal(TestStatus.Failed, result.Tests[0].Status);
        Assert.Contains("beklenen i == 2", failure.Message, StringComparison.Ordinal);
        Assert.Equal(5, failure.Line);
    }
}

public class SampleTestsRunnerTests
{
    [Fact]
    public async Task SampleTestsBothPassWithoutOutput()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "samples", "Testler", "ana.us");
        var result = await TestRunner.RunTextAsync(path, File.ReadAllText(path));
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(2, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Errors);
        Assert.All(result.Tests, item => Assert.Empty(item.Output));
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
