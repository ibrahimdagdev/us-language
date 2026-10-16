using System.Collections.Immutable;
using Us.Core;

namespace Us.Compiler;

public sealed class UsProject
{
    public UsProject(
        string root,
        ProjectManifest manifest,
        ImmutableArray<string> sourcePaths,
        string? entryPath,
        ImmutableArray<PackageReference> dependencies,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Root = root;
        Manifest = manifest;
        SourcePaths = sourcePaths;
        EntryPath = entryPath;
        Dependencies = dependencies;
        Diagnostics = diagnostics;
    }

    public string Root { get; }

    public ProjectManifest Manifest { get; }

    public ImmutableArray<string> SourcePaths { get; }

    public string? EntryPath { get; }

    public ImmutableArray<PackageReference> Dependencies { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
        || Manifest.HasErrors;

    public ProjectTarget Target => Manifest.Target;
}

public static class ProjectLoader
{
    public const string ManifestFileName = "us.proje";
    public const string DefaultEntry = "ana.us";

    private static readonly string[] ExcludedDirectoryNames =
        ["yayin", "bin", "obj", ".git", PackageResolver.CacheDirectoryName, "cikti"];

    public static UsProject Load(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        var diagnostics = new List<Diagnostic>();
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingEntry,
                DiagnosticSeverity.Error,
                $"'{ManifestFileName}' bulunamadı.",
                root,
                new TextSpan(0, 0),
                1,
                1,
                $"Proje kökünde {ManifestFileName} oluşturun."));
            return new UsProject(root, new ProjectManifest(string.Empty, "0.0.0", ProjectTarget.Application, null, [], [.. diagnostics]), [], null, [], [.. diagnostics]);
        }

        var manifest = ProjectManifestParser.ParseFile(manifestPath);
        diagnostics.AddRange(manifest.Diagnostics);

        var sources = new List<string>(DiscoverSources(root));
        if (!manifest.HasErrors)
        {
            var resolved = PackageResolver.Resolve(root, manifest.Packages, manifestPath);
            diagnostics.AddRange(resolved.Diagnostics);
            if (resolved.Succeeded)
            {
                foreach (var package in resolved.Packages)
                {
                    foreach (var path in package.SourcePaths)
                    {
                        sources.Add(path);
                    }
                }
            }
        }

        // Deterministic source order across project + packages.
        sources = sources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => NormalizeKey(root, path), StringComparer.Ordinal)
            .ToList();

        var relativeEntry = string.IsNullOrWhiteSpace(manifest.Entry) ? DefaultEntry : manifest.Entry!;
        var entryPath = Path.GetFullPath(Path.Combine(root, relativeEntry.Replace('/', Path.DirectorySeparatorChar)));
        string? resolvedEntry = null;
        if (manifest.Target == ProjectTarget.Library)
        {
            resolvedEntry = File.Exists(entryPath) ? entryPath : null;
        }
        else if (!File.Exists(entryPath))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingEntry,
                DiagnosticSeverity.Error,
                $"Giriş dosyası '{relativeEntry}' bulunamadı.",
                manifestPath,
                new TextSpan(0, 0),
                1,
                1,
                $"{DefaultEntry} ekleyin veya başlangıç satırını düzeltin."));
        }
        else
        {
            resolvedEntry = entryPath;
        }

        return new UsProject(
            root,
            manifest,
            [.. sources],
            resolvedEntry,
            manifest.Packages,
            [.. diagnostics]);
    }

    public static UsProject? TryLoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(directory, ManifestFileName)))
        {
            return Load(directory);
        }

        return null;
    }

    public static ImmutableArray<string> DiscoverSources(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFiles(root, "*.us", SearchOption.AllDirectories)
            .OrderBy(item => NormalizeKey(root, item), StringComparer.Ordinal))
        {
            if (IsExcluded(root, path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            if (!seen.Add(full))
            {
                continue;
            }

            paths.Add(full);
        }

        return [.. paths];
    }

    public static ImmutableArray<SourceFile> LoadSourceFiles(UsProject project)
    {
        var files = ImmutableArray.CreateBuilder<SourceFile>();
        foreach (var path in project.SourcePaths)
        {
            files.Add(SourceFile.FromUtf8(path, File.ReadAllBytes(path)));
        }

        return files.ToImmutable();
    }

    private static bool IsExcluded(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part => ExcludedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeKey(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Replace('\\', '/');
    }
}
