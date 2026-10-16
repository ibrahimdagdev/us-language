using Us.Core;

namespace Us.Compiler;

/// <summary>
/// Future remote registry client foundation (no online server in v0.2).
/// Provides metadata/hash contracts and integrity verification hooks only.
/// </summary>
public sealed class PackageRegistryMetadata
{
    public PackageRegistryMetadata(string name, string version, string? downloadUrl, string? sha256Hex)
    {
        Name = name;
        Version = version;
        DownloadUrl = downloadUrl;
        Sha256Hex = sha256Hex;
    }

    public string Name { get; }

    public string Version { get; }

    public string? DownloadUrl { get; }

    public string? Sha256Hex { get; }
}

public sealed class PackageRegistryClient
{
    public PackageRegistryClient(Uri? registryBaseUrl = null)
    {
        RegistryBaseUrl = registryBaseUrl;
    }

    public Uri? RegistryBaseUrl { get; }

    /// <summary>Stub: remote metadata lookup is not available offline.</summary>
    public PackageRegistryMetadata? TryGetMetadata(string name, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _ = RegistryBaseUrl;
        return null;
    }

    /// <summary>Verify SHA-256 of a local archive against expected hex.</summary>
    public Diagnostic? VerifyIntegrity(string archivePath, string expectedSha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);
        if (!File.Exists(archivePath))
        {
            return new Diagnostic(
                DiagnosticCodes.PackageNotFound,
                DiagnosticSeverity.Error,
                $"Paket arşivi bulunamadı: '{archivePath}'.",
                archivePath,
                new TextSpan(0, 1),
                1,
                1,
                null);
        }

        var actual = PackageCache.ComputeSha256Hex(archivePath);
        var expected = expectedSha256Hex.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return new Diagnostic(
                DiagnosticCodes.PackageIntegrity,
                DiagnosticSeverity.Error,
                "Paket bütünlük doğrulaması başarısız (SHA-256 uyuşmuyor).",
                archivePath,
                new TextSpan(0, 1),
                1,
                1,
                "Arşivi yeniden indirin veya hash değerini kontrol edin.");
        }

        return null;
    }

    /// <summary>Stub: download is deferred until a registry URL protocol is defined.</summary>
    public Task<(string? Path, Diagnostic? Error)> DownloadAsync(
        PackageRegistryMetadata metadata,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Diagnostic error = new(
            DiagnosticCodes.PackageNotFound,
            DiagnosticSeverity.Error,
            "Uzak paket indirme v0.2.0'da desteklenmiyor (yalnız yerel kurulum).",
            metadata.DownloadUrl ?? RegistryBaseUrl?.AbsoluteUri ?? string.Empty,
            new TextSpan(0, 1),
            1,
            1,
            "us paket kur <yerel.zip> kullanın.");
        return Task.FromResult<(string? Path, Diagnostic? Error)>((null, error));
    }
}
