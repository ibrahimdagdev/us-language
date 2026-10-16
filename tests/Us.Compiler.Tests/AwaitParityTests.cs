using System.Text;
using Us.Compiler;
using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Compiler.Tests;

public class AwaitParityTests
{
    [Fact]
    public void AwaitCompletedAsyncFunctionMatches()
    {
        PhaseAParity.AssertParity("""
            afonksiyon değer(): tamsayı
            {
                => 42
            }
            sonuç: tamsayı = bekle değer()
            syaz "{sonuç}"
            """);
    }

    [Fact]
    public void AwaitDelayThenWriteMatches()
    {
        PhaseAParity.AssertParity("""
            afonksiyon bekleVeYaz()
            {
                bekle 20.milisaniye
                syaz "Tamam"
            }
            bekle bekleVeYaz()
            """);
    }

    [Fact]
    public void NestedAsyncAndLocalsSurviveSuspension()
    {
        PhaseAParity.AssertParity("""
            afonksiyon iç(): tamsayı
            {
                a: tamsayı = 1
                bekle 10.milisaniye
                => a + 2
            }
            afonksiyon dış(): tamsayı
            {
                => bekle iç()
            }
            sonuç: tamsayı = bekle dış()
            syaz "{sonuç}"
            """);
    }

    [Fact]
    public void AsyncExceptionPropagatesAsRuntimeError()
    {
        var source = """
            afonksiyon patla()
            {
                fırlat @Hata("patladı")
            }
            bekle patla()
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var result = ParityHarness.Compare(source, ParityHarnessTests.RunInterpreter);
        Assert.Empty(result.EmitDiagnostics);
        Assert.NotEqual(0, result.CompiledExitCode);
        var text = result.CompiledStderr + result.CompiledStdout;
        Assert.True(
            text.Contains("patladı", StringComparison.Ordinal) || text.Contains("UsThrown", StringComparison.Ordinal),
            text);
    }

    [Fact]
    public void EmittedArtifactUsesUsEmitAsyncNotBusyWait()
    {
        var source = """
            afonksiyon iş()
            {
                bekle 5.milisaniye
                syaz "ok"
            }
            bekle iş()
            """.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bound = Binder.Bind(SourceFile.FromText("await.us", source));
        var emit = new DotNetEmitBackend().Emit(bound);
        Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
        Assert.False(string.IsNullOrWhiteSpace(emit.ArtifactPath));
        var dll = emit.ArtifactPath!;
        var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(dll));
        Assert.Contains("UsEmitAsync", ascii, StringComparison.Ordinal);
        Assert.Contains("UsAsyncBox", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("SpinWait", ascii, StringComparison.Ordinal);
        var run = EmittedProcess.Run(dll);
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("ok", run.StandardOutput, StringComparison.Ordinal);
    }
}
