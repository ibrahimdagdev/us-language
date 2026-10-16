using System.Collections.Immutable;
using Us.Core;
using Us.Semantics;

namespace Us.Compiler;

public sealed class BackendResult
{
    public BackendResult(bool success, ImmutableArray<Diagnostic> diagnostics, string? artifactPath = null)
    {
        Success = success;
        Diagnostics = diagnostics;
        ArtifactPath = artifactPath;
    }

    public bool Success { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public string? ArtifactPath { get; }

    public static BackendResult Ok(string? artifactPath = null) =>
        new(true, [], artifactPath);

    public static BackendResult Fail(params Diagnostic[] diagnostics) =>
        new(false, [.. diagnostics], null);
}

public interface IBackend
{
    BackendResult Emit(BoundCompilation compilation, string? outputDirectory = null);
}

public sealed class NoopBackend : IBackend
{
    public BackendResult Emit(BoundCompilation compilation, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        return BackendResult.Ok();
    }
}
