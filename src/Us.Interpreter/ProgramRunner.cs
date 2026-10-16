using Us.Core;
using Us.Runtime;

namespace Us.Interpreter;

public sealed class ExecutionResult
{
    public ExecutionResult(bool evaluated, IReadOnlyList<Diagnostic> diagnostics, ListOutput output, UsValue value, int rootCellCount)
    {
        Evaluated = evaluated;
        Diagnostics = diagnostics;
        Output = output;
        Value = value;
        RootCellCount = rootCellCount;
    }

    public bool Evaluated { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public ListOutput Output { get; }

    public UsValue Value { get; }

    public int RootCellCount { get; }

    public UsContainer? Container { get; internal set; }

    public int DirectConstructions { get; internal set; }

    public IUsConfiguration? Configuration { get; internal set; }

    public IUsLog? Log { get; internal set; }
}

public static class ProgramRunner
{
    public static ExecutionResult Run(SourceFile file) =>
        Task.Run(async () => await RunAsync(file).ConfigureAwait(false)).GetAwaiter().GetResult();

    public static ExecutionResult Run(SourceFile file, UsHost host) =>
        Task.Run(async () => await RunAsync(file, null, host).ConfigureAwait(false)).GetAwaiter().GetResult();

    public static async ValueTask<ExecutionResult> RunAsync(SourceFile file, IUsScheduler? scheduler = null, UsHost? host = null) =>
        await RunAsync([file], file, scheduler, host).ConfigureAwait(false);

    public static async ValueTask<ExecutionResult> RunAsync(IReadOnlyList<SourceFile> files, SourceFile entry, IUsScheduler? scheduler = null, UsHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(entry);
        var compiled = UsCompile.Front(files);
        var bound = compiled.Compilation;
        var errors = compiled.Errors;
        var output = new ListOutput();
        if (errors.Length > 0)
        {
            return new ExecutionResult(false, errors, output, UsValue.Unit, 0);
        }

        if (host is not null && scheduler is not null)
        {
            host = new UsHost
            {
                Input = host.Input,
                Clock = host.Clock,
                Files = host.Files,
                Configuration = host.Configuration,
                Log = host.Log,
                Scheduler = scheduler,
                Transport = host.Transport,
            };
        }

        var evaluator = host is null ? new Evaluator(output, scheduler) : new Evaluator(output, host);
        var value = await evaluator.ExecuteAsync(bound, entry).ConfigureAwait(false);
        return new ExecutionResult(true, evaluator.RuntimeDiagnostics, output, value, evaluator.RootCellCount)
        {
            Container = evaluator.Container,
            DirectConstructions = evaluator.DirectConstructions,
            Configuration = evaluator.Host.Configuration,
            Log = evaluator.Host.Log,
        };
    }

    public static ExecutionResult RunText(string path, string text) =>
        Run(SourceFile.FromText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal)));

    public static ExecutionResult RunText(string path, string text, UsHost host) =>
        Run(SourceFile.FromText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal)), host);

    public static ValueTask<ExecutionResult> RunTextAsync(string path, string text, IUsScheduler? scheduler = null, UsHost? host = null) =>
        RunAsync(SourceFile.FromText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal)), scheduler, host);
}
