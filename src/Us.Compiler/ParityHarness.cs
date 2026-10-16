using System.Collections.Immutable;
using System.Text;
using Us.Core;
using Us.Semantics;

namespace Us.Compiler;

/// <summary>
/// Compares interpreter-observed output with a separately executed emitted DLL.
/// Interpreter execution is supplied by the caller so Us.Compiler does not reference Us.Interpreter.
/// </summary>
public sealed class ParityRunResult
{
    public ParityRunResult(
        bool matched,
        int interpreterExitCode,
        string interpreterStdout,
        string interpreterStderr,
        int compiledExitCode,
        string compiledStdout,
        string compiledStderr,
        string? artifactPath,
        IReadOnlyList<Diagnostic> emitDiagnostics,
        string? failureReason)
    {
        Matched = matched;
        InterpreterExitCode = interpreterExitCode;
        InterpreterStdout = interpreterStdout;
        InterpreterStderr = interpreterStderr;
        CompiledExitCode = compiledExitCode;
        CompiledStdout = compiledStdout;
        CompiledStderr = compiledStderr;
        ArtifactPath = artifactPath;
        EmitDiagnostics = emitDiagnostics;
        FailureReason = failureReason;
    }

    public bool Matched { get; }

    public int InterpreterExitCode { get; }

    public string InterpreterStdout { get; }

    public string InterpreterStderr { get; }

    public int CompiledExitCode { get; }

    public string CompiledStdout { get; }

    public string CompiledStderr { get; }

    public string? ArtifactPath { get; }

    public IReadOnlyList<Diagnostic> EmitDiagnostics { get; }

    public string? FailureReason { get; }
}

public readonly record struct InterpreterObservation(int ExitCode, string StandardOutput, string StandardError);

public static class ParityHarness
{
    public static ParityRunResult Compare(
        string source,
        Func<string, InterpreterObservation> runInterpreter,
        string? path = null,
        int timeoutMilliseconds = 15000,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(runInterpreter);
        path ??= "parity.us";
        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        var interpreted = runInterpreter(source);
        var bound = Compiler.BindOnly([SourceFile.FromText(path, source)]);
        if (bound.Diagnostics.HasErrors)
        {
            return new ParityRunResult(
                false,
                interpreted.ExitCode,
                Normalize(interpreted.StandardOutput),
                Normalize(interpreted.StandardError),
                -1,
                string.Empty,
                string.Empty,
                null,
                bound.Diagnostics.ToImmutableArray(),
                "Bağlama hataları: " + string.Join("; ", bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));
        }

        var output = Path.Combine(Path.GetTempPath(), "us-parity-" + Guid.NewGuid().ToString("N"));
        var emit = new DotNetEmitBackend().Emit(bound, output);
        if (!emit.Success || emit.ArtifactPath is null)
        {
            return new ParityRunResult(
                false,
                interpreted.ExitCode,
                Normalize(interpreted.StandardOutput),
                Normalize(interpreted.StandardError),
                -1,
                string.Empty,
                string.Empty,
                null,
                emit.Diagnostics,
                "Emit başarısız: " + string.Join("; ", emit.Diagnostics.Select(item => item.Message)));
        }

        EmittedProcessResult process;
        try
        {
            process = EmittedProcess.Run(emit.ArtifactPath, timeoutMilliseconds, standardInput, environment);
        }
        catch (TimeoutException exception)
        {
            return new ParityRunResult(
                false,
                interpreted.ExitCode,
                Normalize(interpreted.StandardOutput),
                Normalize(interpreted.StandardError),
                -1,
                string.Empty,
                exception.Message,
                emit.ArtifactPath,
                emit.Diagnostics,
                "Derlenmiş süreç zaman aşımı.");
        }

        var expectedOut = Normalize(interpreted.StandardOutput);
        var actualOut = Normalize(process.StandardOutput);
        var expectedErr = Normalize(interpreted.StandardError);
        var actualErr = Normalize(process.StandardError);
        var matched = interpreted.ExitCode == process.ExitCode
            && string.Equals(expectedOut, actualOut, StringComparison.Ordinal)
            && string.Equals(expectedErr, actualErr, StringComparison.Ordinal);

        string? reason = null;
        if (!matched)
        {
            var builder = new StringBuilder();
            if (interpreted.ExitCode != process.ExitCode)
            {
                builder.Append($"exit {interpreted.ExitCode} vs {process.ExitCode}; ");
            }

            if (!string.Equals(expectedOut, actualOut, StringComparison.Ordinal))
            {
                builder.Append($"stdout expected={Quote(expectedOut)} actual={Quote(actualOut)}; ");
            }

            if (!string.Equals(expectedErr, actualErr, StringComparison.Ordinal))
            {
                builder.Append($"stderr expected={Quote(expectedErr)} actual={Quote(actualErr)}; ");
            }

            reason = builder.ToString().Trim();
        }

        return new ParityRunResult(
            matched,
            interpreted.ExitCode,
            expectedOut,
            expectedErr,
            process.ExitCode,
            actualOut,
            actualErr,
            emit.ArtifactPath,
            emit.Diagnostics,
            reason);
    }

    public static string Normalize(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length > 0 && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        return text;
    }

    private static string Quote(string text) => "\"" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
