using System.IO.Compression;
using System.Text;
using Us.Compiler;
using Us.Core;

namespace Us.Compiler.Tests;

public class ZipSlipTests
{
    [Fact]
    public void MaliciousArchiveWritesNothing()
    {
        var destination = Path.Combine(Path.GetTempPath(), "us-zip-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var stream = BuildArchive(("../escape.txt", "kaçak"), ("ok.txt", "iyi"));
            var result = PackageArchive.Extract(stream, destination);
            Assert.False(result.Succeeded);
            Assert.Equal(DiagnosticCodes.PackagePathEscape, Assert.Single(result.Diagnostics).Code);
            Assert.Empty(result.WrittenFiles);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public void AbsoluteEntryWritesNothing()
    {
        var destination = Path.Combine(Path.GetTempPath(), "us-zip-abs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var stream = BuildArchive(("C:/Windows/evil.txt", "kaçak"));
            var result = PackageArchive.Extract(stream, destination);
            Assert.False(result.Succeeded);
            Assert.Equal(DiagnosticCodes.PackagePathEscape, Assert.Single(result.Diagnostics).Code);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public void ValidArchiveWritesOnlyUnderTarget()
    {
        var destination = Path.Combine(Path.GetTempPath(), "us-zip-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var stream = BuildArchive(("lib/mod.us", "fonksiyon a(): tamsayı => 1"), ("oku.txt", "merhaba"));
            var result = PackageArchive.Extract(stream, destination);
            Assert.True(result.Succeeded);
            Assert.Empty(result.Diagnostics);
            Assert.Contains("lib/mod.us", result.WrittenFiles);
            Assert.Contains("oku.txt", result.WrittenFiles);
            Assert.Equal("merhaba", File.ReadAllText(Path.Combine(destination, "oku.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "lib", "mod.us")));
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public void MixedSeparatorTraversalIsRejected()
    {
        var destination = Path.Combine(Path.GetTempPath(), "us-zip-mix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var stream = BuildArchive(("foo/../../evil.us", "kaçak"));
            var result = PackageArchive.Extract(stream, destination);
            Assert.False(result.Succeeded);
            Assert.Equal(DiagnosticCodes.PackagePathEscape, Assert.Single(result.Diagnostics).Code);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    [Fact]
    public void DuplicateNormalizedDestinationIsRejected()
    {
        var destination = Path.Combine(Path.GetTempPath(), "us-zip-dup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            using var stream = BuildArchive(("lib/a.us", "bir"), ("lib\\a.us", "iki"));
            var result = PackageArchive.Extract(stream, destination);
            Assert.False(result.Succeeded);
            Assert.Equal(DiagnosticCodes.PackageMalformed, Assert.Single(result.Diagnostics).Code);
            Assert.Empty(result.WrittenFiles);
        }
        finally
        {
            Directory.Delete(destination, true);
        }
    }

    private static MemoryStream BuildArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
