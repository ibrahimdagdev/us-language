using System.IO.Compression;
using Us.Core;

namespace Us.Compiler;

public sealed class PackageExtractResult
{
    public PackageExtractResult(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<string> writtenFiles)
    {
        Diagnostics = diagnostics;
        WrittenFiles = writtenFiles;
    }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public IReadOnlyList<string> WrittenFiles { get; }

    public bool Succeeded => Diagnostics.Count == 0;
}

public static class PackageArchive
{
    public static PackageExtractResult Extract(Stream archive, string destinationRoot)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var root = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(root);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var diagnostics = new List<Diagnostic>();
        var planned = new List<(string FullPath, string Relative, ZipArchiveEntry Entry)>();
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }

                if (!TryResolveEntry(root, rootPrefix, entry.FullName, out var fullPath, out var relative))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PackagePathEscape,
                        DiagnosticSeverity.Error,
                        "Paket girişi hedef dizinin dışına yazamaz.",
                        destinationRoot,
                        new TextSpan(0, 1),
                        1,
                        1,
                        "Arşivdeki yolları hedef kökün altında tutun."));
                    return new PackageExtractResult(diagnostics, []);
                }

                if (destinations.TryGetValue(relative, out var prior))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PackageMalformed,
                        DiagnosticSeverity.Error,
                        $"Paket arşivinde yinelenen hedef yol: '{relative}' ('{prior}' ve '{entry.FullName}').",
                        destinationRoot,
                        new TextSpan(0, 1),
                        1,
                        1,
                        "Arşiv girişlerini tekilleştirin."));
                    return new PackageExtractResult(diagnostics, []);
                }

                destinations[relative] = entry.FullName;
                planned.Add((fullPath, relative, entry));
            }

            var written = new List<string>();
            foreach (var (fullPath, relative, entry) in planned)
            {
                var parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                entry.ExtractToFile(fullPath, overwrite: true);
                written.Add(relative);
            }

            return new PackageExtractResult(diagnostics, written);
        }
    }

    private static bool TryResolveEntry(
        string root,
        string rootPrefix,
        string entryName,
        out string fullPath,
        out string relative)
    {
        fullPath = string.Empty;
        relative = string.Empty;
        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/') ||
            normalized.Contains("../", StringComparison.Ordinal) ||
            normalized.Contains("..\\", StringComparison.Ordinal) ||
            normalized == ".." ||
            normalized.EndsWith("/..", StringComparison.Ordinal) ||
            (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':'))
        {
            return false;
        }

        // Reject mixed-separator traversal forms such as "..\foo" after slash normalize already covered;
        // also reject empty / "."-only segments that could obscure identity.
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is ".." or "."))
        {
            return false;
        }

        relative = string.Join('/', segments);
        if (relative.Length == 0)
        {
            return false;
        }

        var combined = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments)));
        if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = combined;
        return true;
    }
}
