using Us.Core;
using Us.Interpreter;
using Us.Runtime;
using Us.Semantics;

namespace Us.Conformance.Tests;

public static class ConformanceRunner
{
    public static ConformanceResult Run(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            return new ConformanceResult(0, true);
        }

        var valid = Directory.EnumerateFiles(directory, "valid.us", SearchOption.AllDirectories).ToArray();
        var invalid = Directory.EnumerateFiles(directory, "invalid.us", SearchOption.AllDirectories).ToArray();
        var cases = valid.Length + invalid.Length;
        if (cases == 0)
        {
            return new ConformanceResult(0, true);
        }

        foreach (var path in valid)
        {
            var file = SourceFile.FromText(path, File.ReadAllText(path));
            var parsed = global::Us.Parser.Parser.Parse(file);
            var bound = Binder.Bind(file, parsed.Root);
            var diagnostics = parsed.Diagnostics.ToImmutableArray()
                .Concat(bound.Diagnostics.ToImmutableArray())
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .ToArray();
            if (diagnostics.Length > 0)
            {
                var detail = string.Join("; ", diagnostics.Select(item => $"{item.Code} {item.Span.Start} {item.Span.Length}"));
                return new ConformanceResult(cases, false, path + ": " + detail);
            }

            var folder = Path.GetDirectoryName(path)!;
            var outputPath = Path.Combine(folder, "output.txt");
            var logPath = Path.Combine(folder, "log.txt");
            if (File.Exists(outputPath) || File.Exists(logPath))
            {
                var host = new UsHost
                {
                    Clock = new FixedClock(new DateTime(2026, 10, 7)),
                    Files = new MemoryFileSystem(),
                    Configuration = new MemoryConfiguration(File.Exists(Path.Combine(folder, "uygulamaAyar.json")) ? File.ReadAllText(Path.Combine(folder, "uygulamaAyar.json")) : null),
                    Input = new QueueInput(File.Exists(Path.Combine(folder, "input.txt")) ? File.ReadAllLines(Path.Combine(folder, "input.txt")) : []),
                    Log = new ListLog(),
                    Scheduler = RealScheduler.Instance,
                };
                var execution = ProgramRunner.Run(file, host);
                if (!execution.Evaluated || execution.Diagnostics.Count > 0)
                {
                    var detail = string.Join("; ", execution.Diagnostics.Select(item => item.Code + " " + item.Message));
                    return new ConformanceResult(cases, false, path + " çalışmadı: " + detail);
                }

                var expectedOutput = File.Exists(outputPath) ? File.ReadAllLines(outputPath) : [];
                if (File.Exists(outputPath) && !execution.Output.Lines.SequenceEqual(expectedOutput))
                {
                    return new ConformanceResult(cases, false, path + " çıktı [" + string.Join(" | ", execution.Output.Lines) + "] beklenen [" + string.Join(" | ", expectedOutput) + "]");
                }

                if (File.Exists(logPath))
                {
                    var actualLog = host.Log.Records.Select(record => Level(record.Level) + " " + record.Message).ToArray();
                    var expectedLog = File.ReadAllLines(logPath);
                    if (!actualLog.SequenceEqual(expectedLog))
                    {
                        return new ConformanceResult(cases, false, path + " günlük [" + string.Join(" | ", actualLog) + "] beklenen [" + string.Join(" | ", expectedLog) + "]");
                    }
                }
            }
        }

        foreach (var path in invalid)
        {
            var expectedPath = Path.Combine(Path.GetDirectoryName(path)!, "diagnostics.txt");
            if (!File.Exists(expectedPath))
            {
                return new ConformanceResult(cases, false, expectedPath + " yok");
            }

            var actual = Analyze(path)
                .Select(item => $"{item.Code} {item.Span.Start} {item.Span.Length}")
                .ToArray();
            var expected = File.ReadAllLines(expectedPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToArray();
            if (!actual.SequenceEqual(expected))
            {
                return new ConformanceResult(cases, false, path + " beklenen [" + string.Join(" | ", expected) + "] bulunan [" + string.Join(" | ", actual) + "]");
            }
        }

        return new ConformanceResult(cases, true);
    }

    private static string Level(UsLogLevel level) => level switch
    {
        UsLogLevel.Trace => "iz",
        UsLogLevel.Information => "bilgi",
        UsLogLevel.Warning => "uyarı",
        UsLogLevel.Error => "hata",
        UsLogLevel.Critical => "kritik",
        _ => string.Empty,
    };

    private static Diagnostic[] Analyze(string path)
    {
        var file = SourceFile.FromText(path, File.ReadAllText(path));
        var parsed = global::Us.Parser.Parser.Parse(file);
        var bound = Binder.Bind(file, parsed.Root);
        return parsed.Diagnostics.ToImmutableArray()
            .Concat(bound.Diagnostics.ToImmutableArray())
            .Where(item => item.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }
}

public readonly record struct ConformanceResult(int CaseCount, bool Succeeded, string Detail = "");
