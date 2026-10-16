using System.Security.Cryptography;
using Us.Core;

namespace Us.Compiler;

/// <summary>Local package cache layout under <c>paketler/{name}/{version}/</c>.</summary>
public static class PackageCache
{
    public static PackageInstallResult InstallFromArchive(
        string projectRoot,
        string archivePath,
        string? nameOverride = null,
        string? versionOverride = null,
        string? expectedSha256Hex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        projectRoot = Path.GetFullPath(projectRoot);
        archivePath = Path.GetFullPath(archivePath);

        if (!File.Exists(archivePath))
        {
            return Fail(
                DiagnosticCodes.PackageNotFound,
                $"Paket arşivi bulunamadı: '{archivePath}'.",
                archivePath,
                "Geçerli bir .zip yolu verin.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256Hex))
        {
            var actual = ComputeSha256Hex(archivePath);
            if (!string.Equals(actual, NormalizeHex(expectedSha256Hex), StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    DiagnosticCodes.PackageIntegrity,
                    "Paket bütünlük doğrulaması başarısız (SHA-256 uyuşmuyor).",
                    archivePath,
                    "Arşivi yeniden indirin veya hash değerini kontrol edin.");
            }
        }

        var staging = Path.Combine(Path.GetTempPath(), "us-pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var stream = File.OpenRead(archivePath))
            {
                var extracted = PackageArchive.Extract(stream, staging);
                if (!extracted.Succeeded)
                {
                    return new PackageInstallResult(null, null, null, extracted.Diagnostics, []);
                }
            }

            var identity = ResolveIdentity(staging, nameOverride, versionOverride, archivePath);
            if (identity.Diagnostics.Count > 0)
            {
                return new PackageInstallResult(null, null, null, identity.Diagnostics, []);
            }

            var destination = PackageResolver.PackageRoot(projectRoot, identity.Name!, identity.Version!);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            FinalizeStaging(staging, destination);
            staging = string.Empty; // moved/copied; skip cleanup delete
            var sources = Directory.EnumerateFiles(destination, "*.us", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(destination, path).Replace('\\', '/'))
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            return new PackageInstallResult(identity.Name, identity.Version, destination, [], sources);
        }
        finally
        {
            if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging))
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (IOException)
                {
                    // best-effort
                }
            }
        }
    }

    public static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeHex(string hex) =>
        hex.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);

    private static (string? Name, string? Version, IReadOnlyList<Diagnostic> Diagnostics) ResolveIdentity(
        string stagingRoot,
        string? nameOverride,
        string? versionOverride,
        string archivePath)
    {
        string? name = nameOverride;
        string? version = versionOverride;
        var manifestPath = Path.Combine(stagingRoot, ProjectLoader.ManifestFileName);
        if (File.Exists(manifestPath))
        {
            var manifest = ProjectManifestParser.ParseFile(manifestPath);
            if (manifest.HasErrors)
            {
                return (null, null, manifest.Diagnostics);
            }

            name ??= string.IsNullOrWhiteSpace(manifest.Name) ? null : manifest.Name;
            version ??= string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version == "0.0.0"
                ? null
                : manifest.Version;
        }

        name ??= Path.GetFileNameWithoutExtension(archivePath);
        version ??= "1.0.0";
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null,
            [
                new Diagnostic(
                    DiagnosticCodes.PackageMalformed,
                    DiagnosticSeverity.Error,
                    "Paket adı belirlenemedi.",
                    archivePath,
                    new TextSpan(0, 1),
                    1,
                    1,
                    "us.proje içinde proje satırı veya ad parametresi verin."),
            ]);
        }

        return (name, version, []);
    }

    private static void FinalizeStaging(string staging, string destination)
    {
        try
        {
            Directory.Move(staging, destination);
        }
        catch (IOException)
        {
            CopyDirectory(staging, destination);
            Directory.Delete(staging, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static PackageInstallResult Fail(string code, string message, string path, string suggestion) =>
        new(null, null, null,
        [
            new Diagnostic(code, DiagnosticSeverity.Error, message, path, new TextSpan(0, 1), 1, 1, suggestion),
        ],
        []);
}

public sealed class PackageInstallResult
{
    public PackageInstallResult(
        string? name,
        string? version,
        string? root,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> relativeSources)
    {
        Name = name;
        Version = version;
        Root = root;
        Diagnostics = diagnostics;
        RelativeSources = relativeSources;
    }

    public string? Name { get; }

    public string? Version { get; }

    public string? Root { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public IReadOnlyList<string> RelativeSources { get; }

    public bool Succeeded => Diagnostics.Count == 0 && Name is not null && Version is not null && Root is not null;
}
