using Us.Interpreter;
using Us.Runtime;

namespace Us.Interpreter.Tests;

public static class Run
{
    public static ExecutionResult Text(string text) => ProgramRunner.RunText("deneme.us", text);

    public static ExecutionResult Clean(string text)
    {
        var result = Text(text);
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Empty(result.Diagnostics);
        return result;
    }
}

public class EvaluatorSkeletonTests
{
    [Fact]
    public void EmptyProgramReturnsUnit()
    {
        var result = Run.Clean("");
        Assert.Equal(UsValue.Unit, result.Value);
    }

    [Fact]
    public void UnassignedCellReadIsUs1501()
    {
        var cell = new Cell(false);
        var exception = Assert.Throws<UsRuntimeException>(() => cell.Read(1, 1));
        Assert.Equal(UsRuntimeCodes.UnassignedRead, exception.Code);
    }
}

public class ArithmeticEvalTests
{
    [Fact]
    public void PrecedenceMatchesTheBoundTree()
    {
        var result = Run.Clean("sonuç: tamsayı = 2 + 3 * 4\nsyaz \"{sonuç}\"");
        Assert.Equal("14", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void IntegerDivisionTruncatesTowardZero()
    {
        var result = Run.Clean("sonuç: tamsayı = 7 / 2\nsyaz \"{sonuç}\"");
        Assert.Equal("3", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void DivisionByZeroIsReported()
    {
        var result = Run.Text("x: tamsayı = 10 / 0");
        Assert.True(result.Evaluated);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(UsRuntimeCodes.DivisionByZero, diagnostic.Code);
        Assert.Contains("Sıfıra", diagnostic.Message, StringComparison.Ordinal);
        Assert.Empty(result.Output.Lines);
    }

    [Fact]
    public void AndAndOrShortCircuit()
    {
        var and = Run.Clean("sayaç: tamsayı = 0\ndeğer: mantıksal = yanlış && (sayaç = 1) == 1\nsyaz \"{sayaç}\"");
        Assert.Equal("0", Assert.Single(and.Output.Lines));
        var or = Run.Clean("sayaç: tamsayı = 0\ndeğer: mantıksal = doğru || (sayaç = 1) == 1\nsyaz \"{sayaç}\"");
        Assert.Equal("0", Assert.Single(or.Output.Lines));
    }

    [Fact]
    public void IntegerWidensWhenTheTargetIsFloating()
    {
        var result = Run.Clean("a: ondalık = 1\nb: ondalık = a + 2.5\nsyaz \"{b}\"");
        Assert.Equal("3.5", Assert.Single(result.Output.Lines));
    }
}

public class AssignmentEvalTests
{
    [Fact]
    public void AssignedIntegerCanBeRead()
    {
        var result = Run.Clean("yaş: tamsayı = 38\nsyaz \"{yaş}\"");
        Assert.Equal("38", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void IncrementAndCompoundAssignmentUpdateTheCell()
    {
        var result = Run.Clean("sayı: tamsayı = 10\nsayı++\nsayı += 1\nsayı--\nsyaz \"{sayı}\"");
        Assert.Equal("11", Assert.Single(result.Output.Lines));
    }
}

public class BlockEvalTests
{
    [Fact]
    public void InnerCellDoesNotRemainAfterTheBlock()
    {
        var result = Run.Clean("a: tamsayı = 1\n{\n    b: tamsayı = 2\n    a = b\n}\nsyaz \"{a}\"");
        Assert.Equal("2", Assert.Single(result.Output.Lines));
        Assert.Equal(1, result.RootCellCount);
    }
}

public class IfEvalTests
{
    [Fact]
    public void OnlyTheMatchingBranchRuns()
    {
        var result = Run.Clean("""
            yaş: tamsayı = 10
            sayaç: tamsayı = 0
            eğer yaş >= 18
            {
                syaz "Yetişkin"
                sayaç = 1
            }
            edeğilse yaş >= 13
            {
                syaz "Genç"
                sayaç = 2
            }
            değilse
            {
                syaz "Çocuk"
            }
            syaz "{sayaç}"
            """);
        Assert.Equal(["Çocuk", "0"], result.Output.Lines);
    }
}

public class ExpressionIfEvalTests
{
    [Fact]
    public void AdultTextIsChosenAndTheOtherArmDoesNotRun()
    {
        var result = Run.Clean("""
            yaş: tamsayı = 38
            sayaç: metin = "0"
            durum: metin = eğer yaş >= 18 => "Yetişkin" !=> (sayaç = "1")
            syaz durum
            syaz sayaç
            """);
        Assert.Equal(["Yetişkin", "0"], result.Output.Lines);
    }
}

public class WhileEvalTests
{
    [Fact]
    public void FalseWhileDoesNotRunTheBody()
    {
        var result = Run.Clean("sayı: tamsayı = 0\ntekrarla yanlış\n{\n    sayı = 1\n}\nsyaz \"{sayı}\"");
        Assert.Equal("0", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void DoRunsOnceWhenTheConditionIsFalse()
    {
        var result = Run.Clean("sayı: tamsayı = 0\nyap\n{\n    sayı = sayı + 1\n}\ntekrarla yanlış\nsyaz \"{sayı}\"");
        Assert.Equal("1", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void InfiniteLoopStopsAtBreakAndInnerBreakStaysInner()
    {
        var stopped = Run.Clean("sayı: tamsayı = 0\nsonsuzdön\n{\n    sayı++\n    eğer sayı == 3\n    {\n        çık\n    }\n}\nsyaz \"{sayı}\"");
        Assert.Equal("3", Assert.Single(stopped.Output.Lines));
        var nested = Run.Clean("""
            sayaç: tamsayı = 0
            dön 1 ile 3 -> i
            {
                dön 1 ile 3 -> j
                {
                    eğer j == 2
                    {
                        çık
                    }
                    sayaç++
                }
            }
            syaz "{sayaç}"
            """);
        Assert.Equal("3", Assert.Single(nested.Output.Lines));
    }
}

public class RangeEvalTests
{
    [Fact]
    public void ClosedRangeIncludesBothEnds()
    {
        var result = Run.Clean("adet: tamsayı = 0\ndön 1 ile 3 -> i\n{\n    adet++\n}\nsyaz \"{adet}\"");
        Assert.Equal("3", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void StepOfTenFromZeroThroughOneHundredIsElevenValues()
    {
        var result = Run.Clean("adet: tamsayı = 0\ndön 0 ile 100 adım 10 -> i\n{\n    adet++\n}\nsyaz \"{adet}\"");
        Assert.Equal("11", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void DirectionComesFromTheRangeAndTheEndIsNotForced()
    {
        var reverse = Run.Clean("toplam: tamsayı = 0\ndön 5 ile 1 adım 2 -> i\n{\n    toplam += i\n}\nsyaz \"{toplam}\"");
        Assert.Equal("9", Assert.Single(reverse.Output.Lines));
        var uneven = Run.Clean("toplam: tamsayı = 0\ndön 1 ile 6 adım 2 -> i\n{\n    toplam += i\n}\nsyaz \"{toplam}\"");
        Assert.Equal("9", Assert.Single(uneven.Output.Lines));
        var once = Run.Clean("adet: tamsayı = 0\ndön 4 ile 4 -> i\n{\n    adet++\n}\nsyaz \"{adet}\"");
        Assert.Equal("1", Assert.Single(once.Output.Lines));
    }

    [Fact]
    public void DynamicZeroStepIsARuntimeError()
    {
        var result = Run.Text("adım: tamsayı = 0\ndön 1 ile 3 adım adım -> i\n{\n}\n");
        Assert.True(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.ZeroStep);
    }
}

public class OutputEvalTests
{
    [Fact]
    public void InterpolationPrintsOneLine()
    {
        var result = Run.Clean("ad: metin = \"İbrahim\"\nsyaz \"Merhaba {ad}\"");
        Assert.Equal("Merhaba İbrahim", Assert.Single(result.Output.Lines));
    }

    [Fact]
    public void WriteDoesNotEndTheLine()
    {
        var result = Run.Clean("yaz \"Merhaba \"\nsyaz \"İbrahim\"");
        Assert.Equal("Merhaba İbrahim", Assert.Single(result.Output.Lines));
        Assert.Equal(string.Empty, result.Output.Pending);
    }

    [Fact]
    public void TwoHolesKeepTurkishText()
    {
        var result = Run.Clean("ad: metin = \"İbrahim\"\nyaş: tamsayı = 38\nsyaz \"Merhaba {ad}, yaşın {yaş}.\"");
        Assert.Equal("Merhaba İbrahim, yaşın 38.", Assert.Single(result.Output.Lines));
    }
}

public class NullableEvalTests
{
    [Fact]
    public void CoalesceSkipsTheRightArmWhenTheLeftIsPresent()
    {
        var present = Run.Clean("sayaç: tamsayı = 0\nn: tamsayı? = 5\nsonuç: tamsayı = n ?? (sayaç = 9)\nsyaz \"{sonuç}\"\nsyaz \"{sayaç}\"");
        Assert.Equal(["5", "0"], present.Output.Lines);
        var missing = Run.Clean("ad: metin? = ~\ngöster: metin = ad ?? \"Misafir\"\nsyaz göster");
        Assert.Equal("Misafir", Assert.Single(missing.Output.Lines));
    }
}

public class HelloEndToEndTests
{
    [Fact]
    public void SamplePrintsMerhabaIbrahim()
    {
        var path = Path.Combine(FindRoot(), "samples", "Merhaba", "ana.us");
        var result = ProgramRunner.Run(Us.Core.SourceFile.FromText(path, File.ReadAllText(path)));
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Merhaba İbrahim", Assert.Single(result.Output.Lines));
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

public class AdultEndToEndTests
{
    [Fact]
    public void SamplePrintsYetiskin()
    {
        var path = Path.Combine(FindRoot(), "samples", "KontrolAkışı", "ana.us");
        var result = ProgramRunner.Run(Us.Core.SourceFile.FromText(path, File.ReadAllText(path)));
        Assert.True(result.Evaluated);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("Yetişkin", Assert.Single(result.Output.Lines));
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

public class RuntimeExceptionTests
{
    [Fact]
    public void DivisionByZeroStaysInTheResult()
    {
        var result = Run.Text("x: tamsayı = 10 / 0");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("US1502", diagnostic.Code);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
    }
}

public class DoNotRunInvalidTests
{
    [Fact]
    public void TypeMismatchDoesNotEvaluate()
    {
        var result = Run.Text("yaş: tamsayı = \"İbrahim\"\nsyaz \"çalıştı\"");
        Assert.False(result.Evaluated);
        Assert.Empty(result.Output.Lines);
        Assert.Equal(0, result.RootCellCount);
        Assert.Contains(result.Diagnostics, item => item.Code == "US1021");
    }
}

public class IsolationTests
{
    [Fact]
    public void TwoRunsDoNotShareOutput()
    {
        var first = Run.Clean("syaz \"bir\"");
        var second = Run.Clean("syaz \"iki\"");
        Assert.Equal("bir", Assert.Single(first.Output.Lines));
        Assert.Equal("iki", Assert.Single(second.Output.Lines));
    }
}
