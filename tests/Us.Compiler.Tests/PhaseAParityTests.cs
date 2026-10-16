using System.Collections.Immutable;
using Us.Compiler;
using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler.Tests;

public class ClrTypeMapTests
{
    [Theory]
    [InlineData("metin")]
    [InlineData("karakter")]
    [InlineData("tamsayı")]
    [InlineData("utamsayı")]
    [InlineData("ondalık")]
    [InlineData("gondalık")]
    [InlineData("hondalık")]
    [InlineData("mantıksal")]
    [InlineData("tarih")]
    [InlineData("saat")]
    [InlineData("tarihsaat")]
    [InlineData("süre")]
    public void MapsAllScalars(string name)
    {
        var type = new PrimitiveTypeSymbol(name);
        Assert.True(ClrTypeMap.TryMapPrimitive(type, out var clr, out var kind));
        Assert.Equal(typeof(UsValue), clr);
        Assert.NotEqual(UsValueKind.Unit, kind);
    }
}

public class LiteralEmitParityTests
{
    [Theory]
    [InlineData("syaz \"{42}\"\n")]
    [InlineData("syaz \"{3.5}\"\n")]
    [InlineData("syaz \"{doğru}\"\n")]
    [InlineData("syaz \"{yanlış}\"\n")]
    [InlineData("syaz \"abc\"\n")]
    public void LiteralsMatch(string source) => PhaseAParity.AssertParity(source);
}

public class LocalAssignmentParityTests
{
    [Fact]
    public void ReassignmentMatches() => PhaseAParity.AssertParity("""
        x: tamsayı = 1
        x = 2
        syaz "{x}"
        """);
}

public class CompoundAssignParityTests
{
    [Fact]
    public void CompoundAndIncrementMatch() => PhaseAParity.AssertParity("""
        x: tamsayı = 10
        x += 5
        x++
        syaz "{x}"
        """);
}

public class ArithmeticParityTests
{
    [Fact]
    public void ArithmeticMatches() => PhaseAParity.AssertParity("""
        a: tamsayı = 10
        b: tamsayı = 3
        syaz "{a + b}"
        syaz "{a - b}"
        syaz "{a * b}"
        syaz "{a / b}"
        syaz "{a % b}"
        syaz "{-a}"
        """);
}

public class LogicParityTests
{
    [Fact]
    public void CompareShortCircuitAndCoalesceMatch() => PhaseAParity.AssertParity("""
        syaz "{1 < 2}"
        syaz "{doğru && yanlış}"
        syaz "{yanlış || doğru}"
        n: tamsayı? = ~
        syaz "{n ?? 7}"
        """);
}

public class NullableParityTests
{
    [Fact]
    public void NullableScalarMatches() => PhaseAParity.AssertParity("""
        x: tamsayı? = ~
        syaz "{x ?? 0}"
        x = 5
        syaz "{x ?? 0}"
        """);
}

public class IfParityTests
{
    [Fact]
    public void IfElseAndExpressionIfMatch() => PhaseAParity.AssertParity("""
        x: tamsayı = 2
        eğer x > 1
        {
            syaz "büyük"
        }
        değilse
        {
            syaz "küçük"
        }
        durum: metin = eğer x > 1 => "a" !=> "b"
        syaz durum
        """);
}

public class LoopParityTests
{
    [Fact]
    public void RangeLoopAndBreakMatch() => PhaseAParity.AssertParity("""
        toplam: tamsayı = 0
        dön 1 ile 3 -> i
        {
            toplam += i
        }
        syaz "{toplam}"
        n: tamsayı = 0
        tekrarla n < 10
        {
            n++
            eğer n == 3
            {
                çık
            }
        }
        syaz "{n}"
        """);
}

public class ConsoleIntrinsicParityTests
{
    [Fact]
    public void YazMatches() => PhaseAParity.AssertParity("""
        yaz "Mer"
        syaz "haba"
        """);
}

public class PhaseAGateTests
{
    [Fact]
    public void PhaseASurfaceDoesNotEmitUs1900()
    {
        var sources = new[]
        {
            """
            ad: metin = "İbrahim"
            syaz "Merhaba {ad}"
            """,
            """
            x: tamsayı = 1
            x += 2
            syaz "{x * 3}"
            """,
            """
            eğer doğru
            {
                syaz "1"
            }
            """,
            """
            dön 1 ile 2 -> i
            {
                syaz "{i}"
            }
            """,
        };

        foreach (var source in sources)
        {
            var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            var bound = Compiler.BindOnly([SourceFile.FromText("a.us", normalized)]);
            Assert.False(bound.Diagnostics.HasErrors, string.Join("; ", bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));
            var dir = Path.Combine(Path.GetTempPath(), "us-phasea-" + Guid.NewGuid().ToString("N"));
            var emit = new DotNetEmitBackend().Emit(bound, dir);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
            Assert.DoesNotContain(emit.Diagnostics, item => item.Code == DiagnosticCodes.EmitUnsupported);
            PhaseAParity.AssertParity(normalized);
        }
    }
}

internal static class PhaseAParity
{
    public static void AssertParity(string source)
    {
        var result = ParityHarness.Compare(source.Replace("\r\n", "\n", StringComparison.Ordinal), ParityHarnessTests.RunInterpreter);
        Assert.True(result.Matched, result.FailureReason + "\ninterp=" + result.InterpreterStdout + "\ncompiled=" + result.CompiledStdout);
    }

    /// <summary>M20+ helper: bind succeeds, emit must fail with US1900 containing <paramref name="detailFragment"/>.</summary>
    public static void AssertEmitUnsupported(string source, string detailFragment)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bound = Compiler.BindOnly([Us.Core.SourceFile.FromText("gate.us", normalized)]);
        Assert.False(
            bound.Diagnostics.HasErrors,
            string.Join('\n', bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));

        var directory = Path.Combine(Path.GetTempPath(), "us-emit-gate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var emitted = new DotNetEmitBackend().Emit(bound, directory);
            Assert.False(emitted.Success, "unsupported path must not succeed silently");
            Assert.Contains(
                emitted.Diagnostics,
                item => item.Code == Us.Core.DiagnosticCodes.EmitUnsupported
                    && item.Message.Contains(detailFragment, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
