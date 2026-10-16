using System.Collections.Immutable;
using Us.Core;
using Us.Parser;
using Us.Semantics;

namespace Us.Compiler;

public sealed class CompileResult
{
    public CompileResult(bool success, ImmutableArray<Diagnostic> diagnostics, ImmutableArray<string> sourcePaths, string? artifactPath = null)
    {
        Success = success;
        Diagnostics = diagnostics;
        SourcePaths = sourcePaths;
        ArtifactPath = artifactPath;
    }

    public bool Success { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<string> SourcePaths { get; }

    public string? ArtifactPath { get; }
}

public static class Compiler
{
    public static CompileResult Compile(UsProject project, IBackend? backend = null, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.HasErrors)
        {
            return new CompileResult(false, project.Diagnostics, project.SourcePaths);
        }

        var files = ProjectLoader.LoadSourceFiles(project);
        if (files.Length == 0)
        {
            var diagnostic = new Diagnostic(
                DiagnosticCodes.MissingEntry,
                DiagnosticSeverity.Error,
                "Derlenecek kaynak dosya yok.",
                project.Root,
                new TextSpan(0, 0),
                1,
                1,
                "*.us kaynağı ekleyin.");
            return new CompileResult(false, [diagnostic], project.SourcePaths);
        }

        outputDirectory ??= Path.Combine(project.Root, "cikti");
        return Compile(files, backend, outputDirectory, project.SourcePaths);
    }

    public static CompileResult Compile(
        IReadOnlyList<SourceFile> files,
        IBackend? backend = null,
        string? outputDirectory = null,
        ImmutableArray<string>? sourcePaths = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("En az bir kaynak dosya gerekir.", nameof(files));
        }

        var parseDiagnostics = new List<Diagnostic>();
        var units = new List<(SourceFile File, CompilationUnitSyntax Root)>(files.Count);
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
            .ToImmutableArray();
        var paths = sourcePaths ?? [.. files.Select(item => item.Path)];
        if (errors.Length > 0)
        {
            return new CompileResult(false, errors, paths);
        }

        if (backend is null)
        {
            return new CompileResult(true, [], paths);
        }

        var emit = backend.Emit(bound, outputDirectory);
        var combined = errors.Concat(emit.Diagnostics).ToImmutableArray();
        return new CompileResult(emit.Success && combined.Length == 0, combined, paths, emit.ArtifactPath);
    }

    public static BoundCompilation BindOnly(IReadOnlyList<SourceFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var units = new List<(SourceFile File, CompilationUnitSyntax Root)>(files.Count);
        foreach (var file in files)
        {
            var parsed = global::Us.Parser.Parser.Parse(file);
            units.Add((file, parsed.Root));
        }

        return Binder.BindMany(units);
    }
}
