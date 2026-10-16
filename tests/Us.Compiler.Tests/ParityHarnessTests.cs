using System.Text;
using Us.Compiler;
using Us.Interpreter;
using Us.Runtime;

namespace Us.Compiler.Tests;

public class ParityHarnessTests
{
    [Fact]
    public void MerhabaFixtureMatchesInterpreterAndEmittedDll()
    {
        var source = """
            ad: metin = "İbrahim"
            syaz "Merhaba {ad}"
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        var result = ParityHarness.Compare(source, RunInterpreter);
        Assert.True(result.Matched, result.FailureReason);
        Assert.Equal(0, result.CompiledExitCode);
        Assert.Contains("Merhaba İbrahim", result.CompiledStdout, StringComparison.Ordinal);
        Assert.NotNull(result.ArtifactPath);
        Assert.True(File.Exists(result.ArtifactPath));

        var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(result.ArtifactPath));
        Assert.DoesNotContain("ProgramRunner", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("Evaluator", ascii, StringComparison.Ordinal);
    }

    [Fact]
    public void SimpleSyazMatches()
    {
        var result = ParityHarness.Compare("syaz \"Merhaba US!\"\n", RunInterpreter);
        Assert.True(result.Matched, result.FailureReason);
    }

    [Fact]
    public void CompilerParityDocumentExists()
    {
        var root = M15Helpers.FindRepo();
        Assert.True(File.Exists(Path.Combine(root, "docs", "compiler-parity.md")));
    }

    internal static InterpreterObservation RunInterpreter(string source)
    {
        var executed = ProgramRunner.RunText("parity.us", source);
        if (!executed.Evaluated)
        {
            var stderr = string.Join('\n', executed.Diagnostics.Select(item => item.Message));
            return new InterpreterObservation(1, string.Empty, stderr);
        }

        var stdout = FormatOutput(executed.Output);
        var runtimeStderr = executed.Diagnostics.Count == 0
            ? string.Empty
            : string.Join('\n', executed.Diagnostics.Select(item => item.Message));
        return new InterpreterObservation(0, stdout, runtimeStderr);
    }

    private static string FormatOutput(ListOutput output)
    {
        if (output.Lines.Count == 0)
        {
            return output.Pending;
        }

        var text = string.Join('\n', output.Lines);
        if (output.Pending.Length > 0)
        {
            text += '\n' + output.Pending;
        }
        else
        {
            text += '\n';
        }

        return text;
    }
}
