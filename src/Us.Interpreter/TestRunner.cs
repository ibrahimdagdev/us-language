using System.Collections.Immutable;
using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public enum TestStatus
{
    Passed,
    Failed,
    Error,
}

public enum TestFailureKind
{
    None,
    AssertionFailed,
    ExpectedErrorMissing,
    UnexpectedError,
    Internal,
    CompilationFailure,
}

public sealed class UsTestFailure : Exception
{
    public UsTestFailure(TestFailureKind kind, string code, string message, int start, int length)
        : base(message)
    {
        Kind = kind;
        Code = code;
        Start = start;
        Length = length;
    }

    public TestFailureKind Kind { get; }

    public string Code { get; }

    public int Start { get; }

    public int Length { get; }
}

public sealed class TestFailure
{
    public TestFailure(TestFailureKind kind, string code, string message, string? filePath, TextSpan span, int line, int column)
    {
        Kind = kind;
        Code = code;
        Message = message;
        FilePath = filePath;
        Span = span;
        Line = line;
        Column = column;
    }

    public TestFailureKind Kind { get; }

    public string Code { get; }

    public string Message { get; }

    public string? FilePath { get; }

    public TextSpan Span { get; }

    public int Line { get; }

    public int Column { get; }
}

public sealed class TestResult
{
    public TestResult(string name, TestStatus status, string? filePath, int line, int column, TestFailure? failure, IReadOnlyList<string> output)
    {
        Name = name;
        Status = status;
        FilePath = filePath;
        Line = line;
        Column = column;
        Failure = failure;
        Output = output;
    }

    public string Name { get; }

    public TestStatus Status { get; }

    public string? FilePath { get; }

    public int Line { get; }

    public int Column { get; }

    public TestFailure? Failure { get; }

    public IReadOnlyList<string> Output { get; }
}

public sealed class TestRunResult
{
    public TestRunResult(bool compiled, IReadOnlyList<TestResult> tests, IReadOnlyList<Diagnostic> diagnostics)
    {
        Compiled = compiled;
        Tests = tests;
        Diagnostics = diagnostics;
        Passed = tests.Count(item => item.Status == TestStatus.Passed);
        Failed = tests.Count(item => item.Status == TestStatus.Failed);
        Errors = tests.Count(item => item.Status == TestStatus.Error);
    }

    public bool Compiled { get; }

    public IReadOnlyList<TestResult> Tests { get; }

    public int Passed { get; }

    public int Failed { get; }

    public int Errors { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}

public static class UsCompile
{
    public static (Diagnostic[] Errors, BoundCompilation Compilation) Front(SourceFile file) =>
        Front([file]);

    public static (Diagnostic[] Errors, BoundCompilation Compilation) Front(IReadOnlyList<SourceFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("En az bir kaynak dosya gerekir.", nameof(files));
        }

        var parseDiagnostics = new List<Diagnostic>();
        var units = new List<(SourceFile File, global::Us.Parser.CompilationUnitSyntax Root)>(files.Count);
        foreach (var file in files)
        {
            parseDiagnostics.AddRange(file.GetEncodingDiagnostics());
            var parsed = global::Us.Parser.Parser.Parse(file);
            parseDiagnostics.AddRange(parsed.Diagnostics.ToImmutableArray());
            units.Add((file, parsed.Root));
        }

        var bound = Binder.BindMany(units);
        var errors = parseDiagnostics
            .Concat(bound.Diagnostics.ToImmutableArray())
            .Where(item => item.Severity == DiagnosticSeverity.Error)
            .ToArray();
        return (errors, bound);
    }
}

public static class TestRunner
{
    public static IReadOnlyList<TestSymbol> Discover(BoundCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return compilation.Tests;
    }

    public static ValueTask<TestRunResult> RunTextAsync(string path, string text, Func<UsHost>? hostFactory = null, Func<string, bool>? nameFilter = null) =>
        RunAsync(SourceFile.FromText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal)), hostFactory, nameFilter);

    public static ValueTask<TestRunResult> RunAsync(SourceFile file, Func<UsHost>? hostFactory = null, Func<string, bool>? nameFilter = null) =>
        RunAsync((IReadOnlyList<SourceFile>)[file], hostFactory, nameFilter);

    public static ValueTask<TestRunResult> RunAsync(IReadOnlyList<SourceFile> files, Func<UsHost>? hostFactory = null, Func<string, bool>? nameFilter = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("En az bir kaynak dosya gerekir.", nameof(files));
        }

        var compiled = UsCompile.Front(files);
        if (compiled.Errors.Length > 0)
        {
            return new ValueTask<TestRunResult>(new TestRunResult(false, [], compiled.Errors));
        }

        return RunBoundAsync(compiled.Compilation, files[0], hostFactory, nameFilter);
    }

    internal static async ValueTask<TestRunResult> RunBoundAsync(BoundCompilation compilation, SourceFile file, Func<UsHost>? hostFactory = null, Func<string, bool>? nameFilter = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(file);
        var selected = new List<TestSymbol>();
        foreach (var test in compilation.Tests)
        {
            if (nameFilter is null || nameFilter(test.Name))
            {
                selected.Add(test);
            }
        }

        var results = new List<TestResult>();
        foreach (var test in selected)
        {
            results.Add(await RunOne(compilation, file, test, hostFactory).ConfigureAwait(false));
        }

        return new TestRunResult(true, results, []);
    }

    private static async ValueTask<TestResult> RunOne(BoundCompilation compilation, SourceFile file, TestSymbol test, Func<UsHost>? hostFactory)
    {
        var location = Locate(file, test.Span);
        var output = new ListOutput();
        var host = hostFactory?.Invoke() ?? FreshHost();
        var evaluator = new Evaluator(output, host);
        try
        {
            await evaluator.RunTestAsync(compilation, file, test).ConfigureAwait(false);
            return new TestResult(test.Name, TestStatus.Passed, file.Path, location.Line, location.Column, null, output.Lines);
        }
        catch (UsTestFailure failure)
        {
            var place = Locate(file, new TextSpan(failure.Start, failure.Length));
            var detail = new TestFailure(failure.Kind, failure.Code, failure.Message, file.Path, new TextSpan(failure.Start, failure.Length), place.Line, place.Column);
            return new TestResult(test.Name, TestStatus.Failed, file.Path, location.Line, location.Column, detail, output.Lines);
        }
        catch (UsThrown thrown)
        {
            return ErrorResult(test, file, location, output, TestFailureKind.UnexpectedError, UsRuntimeCodes.UserThrown, thrown.Message, thrown.Start, thrown.Length);
        }
        catch (UsRuntimeException exception)
        {
            return ErrorResult(test, file, location, output, TestFailureKind.UnexpectedError, exception.Code, exception.Message, exception.Start, exception.Length);
        }
        catch (Exception)
        {
            return ErrorResult(test, file, location, output, TestFailureKind.Internal, UsRuntimeCodes.InterpreterInvariant, "Yorumlayıcı iç hatası.", test.Span.Start, test.Span.Length);
        }
    }

    private static TestResult ErrorResult(TestSymbol test, SourceFile file, (int Line, int Column) location, ListOutput output, TestFailureKind kind, string code, string message, int start, int length)
    {
        var place = Locate(file, new TextSpan(start, length));
        var detail = new TestFailure(kind, code, message, file.Path, new TextSpan(start, length), place.Line, place.Column);
        return new TestResult(test.Name, TestStatus.Error, file.Path, location.Line, location.Column, detail, output.Lines);
    }

    private static (int Line, int Column) Locate(SourceFile file, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, file.Text.Length);
        return file.Lines.GetLineAndColumn(start);
    }

    private static UsHost FreshHost() => new()
    {
        Input = new QueueInput([]),
        Clock = new FixedClock(new DateTime(2026, 10, 7)),
        Files = new MemoryFileSystem(),
        Configuration = new MemoryConfiguration(null),
        Log = new ListLog(),
        Scheduler = TaskDelayScheduler.Instance,
    };
}
