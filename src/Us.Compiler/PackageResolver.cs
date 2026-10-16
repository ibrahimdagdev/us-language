using System.Collections.Immutable;
using Us.Core;

namespace Us.Compiler;

/// <summary>Resolved local package ready for compile/import source inclusion.</summary>
public sealed class ResolvedPackage
{
    public ResolvedPackage(string name, string version, string root, ImmutableArray<string> sourcePaths)
    {
        Name = name;
        Version = version;
        Root = root;
        SourcePaths = sourcePaths;
    }

    public string Name { get; }

    public string Version { get; }

    public string Root { get; }

    public ImmutableArray<string> SourcePaths { get; }

    public string IdentityKey => Name + "@" + Version;
}

public sealed class PackageResolveResult
{
    public PackageResolveResult(ImmutableArray<ResolvedPackage> packages, ImmutableArray<Diagnostic> diagnostics)
    {
        Packages = packages;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<ResolvedPackage> Packages { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Succeeded => Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

/// <summary>
/// Deterministic local package resolution from <c>paketler/{name}/{version}/</c>.
/// Exact versions only; case-sensitive identity; offline.
/// </summary>
public static class PackageResolver
{
    public const string CacheDirectoryName = "paketler";

    public static string CacheRoot(string projectRoot) =>
        Path.GetFullPath(Path.Combine(projectRoot, CacheDirectoryName));

    public static string PackageRoot(string projectRoot, string name, string version) =>
        Path.GetFullPath(Path.Combine(CacheRoot(projectRoot), name, version));

    public static PackageResolveResult Resolve(UsProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Resolve(project.Root, project.Dependencies, project.Manifest.Diagnostics.Length > 0
            ? Path.Combine(project.Root, ProjectLoader.ManifestFileName)
            : Path.Combine(project.Root, ProjectLoader.ManifestFileName));
    }

    public static PackageResolveResult Resolve(
        string projectRoot,
        IEnumerable<PackageReference> references,
        string? manifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(references);
        projectRoot = Path.GetFullPath(projectRoot);
        manifestPath ??= Path.Combine(projectRoot, ProjectLoader.ManifestFileName);

        var diagnostics = new List<Diagnostic>();
        var selected = new Dictionary<string, string>(StringComparer.Ordinal); // name → version
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<ResolvedPackage>();

        foreach (var reference in DeduplicateExact(references, diagnostics, manifestPath))
        {
            Walk(projectRoot, reference.Name, reference.Version, reference.Span, manifestPath, selected, visiting, visited, resolved, diagnostics);
        }

        // Stable order: name then version (Ordinal).
        resolved.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            return byName != 0
                ? byName
                : string.Compare(left.Version, right.Version, StringComparison.Ordinal);
        });

        return new PackageResolveResult([.. resolved], [.. diagnostics]);
    }

    private static IEnumerable<PackageReference> DeduplicateExact(
        IEnumerable<PackageReference> references,
        List<Diagnostic> diagnostics,
        string manifestPath)
    {
        var byName = new Dictionary<string, PackageReference>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                continue;
            }

            var version = string.IsNullOrWhiteSpace(reference.Version) ? "1.0.0" : reference.Version;
            if (byName.TryGetValue(reference.Name, out var existing))
            {
                if (string.Equals(existing.Version, version, StringComparison.Ordinal))
                {
                    // Exact duplicate — idempotent.
                    continue;
                }

                diagnostics.Add(Error(
                    DiagnosticCodes.PackageConflict,
                    $"'{reference.Name}' için çakışan sürümler: '{existing.Version}' ve '{version}'.",
                    manifestPath,
                    reference.Span,
                    "Aynı paket için tek kesin sürüm kullanın."));
                continue;
            }

            byName[reference.Name] = new PackageReference(reference.Name, version, reference.Span);
        }

        return byName.Values.OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Version, StringComparer.Ordinal);
    }

    private static void Walk(
        string projectRoot,
        string name,
        string version,
        TextSpan span,
        string manifestPath,
        Dictionary<string, string> selected,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<ResolvedPackage> resolved,
        List<Diagnostic> diagnostics)
    {
        var identity = name + "@" + version;
        if (visited.Contains(identity))
        {
            return;
        }

        if (!visiting.Add(identity))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.PackageCycle,
                $"Paket bağımlılık döngüsü: {identity}.",
                manifestPath,
                span,
                "Döngüsel paket bağımlılıklarını kaldırın."));
            return;
        }

        if (selected.TryGetValue(name, out var existing) &&
            !string.Equals(existing, version, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                DiagnosticCodes.PackageConflict,
                $"'{name}' için çakışan sürümler: '{existing}' ve '{version}'.",
                manifestPath,
                span,
                "Aynı paket için tek kesin sürüm kullanın."));
            visiting.Remove(identity);
            return;
        }

        selected[name] = version;
        var root = PackageRoot(projectRoot, name, version);
        if (!Directory.Exists(root))
        {
            // Manifest may list packages before local install; participate only when present.
            visiting.Remove(identity);
            visited.Add(identity);
            return;
        }

        var sources = DiscoverPackageSources(root);
        resolved.Add(new ResolvedPackage(name, version, root, sources));

        foreach (var dependency in ReadPackageDependencies(root))
        {
            Walk(
                projectRoot,
                dependency.Name,
                string.IsNullOrWhiteSpace(dependency.Version) ? "1.0.0" : dependency.Version,
                dependency.Span,
                Path.Combine(root, ProjectLoader.ManifestFileName),
                selected,
                visiting,
                visited,
                resolved,
                diagnostics);
        }

        visiting.Remove(identity);
        visited.Add(identity);
    }

    /// <summary>Strict resolve used by install/restore checks — missing packages are errors.</summary>
    public static PackageResolveResult ResolveRequired(
        string projectRoot,
        IEnumerable<PackageReference> references,
        string? manifestPath = null)
    {
        var result = Resolve(projectRoot, references, manifestPath);
        var diagnostics = result.Diagnostics.ToList();
        var resolvedKeys = new HashSet<string>(result.Packages.Select(item => item.IdentityKey), StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name))
            {
                continue;
            }

            var version = string.IsNullOrWhiteSpace(reference.Version) ? "1.0.0" : reference.Version;
            var key = reference.Name + "@" + version;
            if (resolvedKeys.Contains(key))
            {
                continue;
            }

            if (diagnostics.Any(item => item.Code == DiagnosticCodes.PackageConflict || item.Code == DiagnosticCodes.PackageCycle))
            {
                continue;
            }

            diagnostics.Add(Error(
                DiagnosticCodes.PackageNotFound,
                $"Paket bulunamadı: '{reference.Name}' sürüm '{version}' (beklenen: {CacheDirectoryName}/{reference.Name}/{version}).",
                manifestPath ?? Path.Combine(projectRoot, ProjectLoader.ManifestFileName),
                reference.Span,
                "Paketi yerel arşivden kurun: us paket kur <dosya.zip>"));
        }

        return new PackageResolveResult(result.Packages, [.. diagnostics]);
    }

    private static ImmutableArray<PackageReference> ReadPackageDependencies(string packageRoot)
    {
        var manifestPath = Path.Combine(packageRoot, ProjectLoader.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var manifest = ProjectManifestParser.ParseFile(manifestPath);
        return manifest.Packages;
    }

    private static ImmutableArray<string> DiscoverPackageSources(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(packageRoot, "*.us", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(path => Path.GetRelativePath(packageRoot, path).Replace('\\', '/'), StringComparer.Ordinal),
        ];
    }

    private static Diagnostic Error(string code, string message, string path, TextSpan span, string? suggestion) =>
        new(code, DiagnosticSeverity.Error, message, path, span, 1, 1, suggestion);
}
