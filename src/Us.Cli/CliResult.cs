using System.Collections.Immutable;
using Us.Core;

namespace Us.Cli;

public sealed class CliResult
{
    public CliResult(int exitCode, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> output)
    {
        ExitCode = exitCode;
        Diagnostics = diagnostics;
        Output = output;
    }

    public int ExitCode { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<string> Output { get; }

    public static CliResult Ok(params string[] output) =>
        new(0, [], [.. output]);

    public static CliResult Fail(int exitCode, IEnumerable<Diagnostic> diagnostics, params string[] output) =>
        new(exitCode, [.. diagnostics], [.. output]);
}

public sealed class CliHost
{
    public CliHost(string workingDirectory)
    {
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        Out = new StringWriter();
        Error = new StringWriter();
        In = TextReader.Null;
    }

    public string WorkingDirectory { get; }

    public TextWriter Out { get; init; }

    public TextWriter Error { get; init; }

    public TextReader In { get; init; }
}
