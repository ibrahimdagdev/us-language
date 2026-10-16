using System.Collections.Immutable;
using Us.Core;

namespace Us.Compiler;

public enum ProjectTarget
{
    Application,
    Library,
    Web,
}

public sealed class PackageReference
{
    public PackageReference(string name, string version, TextSpan span)
    {
        Name = name;
        Version = version;
        Span = span;
    }

    public string Name { get; }

    public string Version { get; }

    public TextSpan Span { get; }
}

public sealed class ProjectManifest
{
    public ProjectManifest(
        string name,
        string version,
        ProjectTarget target,
        string? entry,
        ImmutableArray<PackageReference> packages,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Name = name;
        Version = version;
        Target = target;
        Entry = entry;
        Packages = packages;
        Diagnostics = diagnostics;
    }

    public string Name { get; }

    public string Version { get; }

    public ProjectTarget Target { get; }

    public string? Entry { get; }

    public ImmutableArray<PackageReference> Packages { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool HasErrors => Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
}

public static class ProjectManifestParser
{
    public static ProjectManifest Parse(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        text ??= string.Empty;
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var diagnostics = new List<Diagnostic>();
        string? name = null;
        string? version = null;
        ProjectTarget? target = null;
        string? entry = null;
        var packages = ImmutableArray.CreateBuilder<PackageReference>();
        var offset = 0;
        var lineNumber = 1;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var lineStart = offset;
            offset += rawLine.Length + 1;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                lineNumber++;
                continue;
            }

            if (!TryParseLine(line, out var key, out var values))
            {
                diagnostics.Add(Error(path, DiagnosticCodes.UnknownProjectKey, $"'{line}' proje bildirimi çözülemedi.", lineStart, Math.Max(line.Length, 1), lineNumber, 1, "proje, sürüm, hedef, başlangıç veya paket satırı yazın."));
                lineNumber++;
                continue;
            }

            var span = new TextSpan(lineStart, rawLine.Length);
            switch (key)
            {
                case "proje":
                    name = values.Count > 0 ? values[0] : string.Empty;
                    break;
                case "sürüm":
                    version = values.Count > 0 ? values[0] : string.Empty;
                    break;
                case "hedef":
                    target = values.Count > 0 ? ParseTarget(values[0], path, span, lineNumber, diagnostics) : null;
                    break;
                case "başlangıç":
                    entry = values.Count > 0 ? values[0] : string.Empty;
                    break;
                case "paket":
                    if (values.Count >= 1)
                    {
                        var packageVersion = values.Count >= 2 ? values[1] : "1.0.0";
                        packages.Add(new PackageReference(values[0], packageVersion, span));
                    }

                    break;
                default:
                    diagnostics.Add(Error(path, DiagnosticCodes.UnknownProjectKey, $"'{key}' proje anahtarı bilinmiyor.", span.Start, Math.Max(span.Length, 1), lineNumber, 1, "proje, sürüm, hedef, başlangıç veya paket kullanın."));
                    break;
            }

            lineNumber++;
        }

        return new ProjectManifest(
            name ?? string.Empty,
            version ?? "0.0.0",
            target ?? ProjectTarget.Application,
            entry,
            packages.ToImmutable(),
            [.. diagnostics]);
    }

    public static ProjectManifest ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        return Parse(path, text);
    }

    private static ProjectTarget? ParseTarget(string value, string path, TextSpan span, int line, List<Diagnostic> diagnostics)
    {
        switch (value)
        {
            case "uygulama":
                return ProjectTarget.Application;
            case "kütüphane":
                return ProjectTarget.Library;
            case "web":
                return ProjectTarget.Web;
            default:
                diagnostics.Add(Error(path, DiagnosticCodes.UnknownProjectKey, $"'{value}' hedefi bilinmiyor.", span.Start, Math.Max(span.Length, 1), line, 1, "uygulama, kütüphane veya web yazın."));
                return null;
        }
    }

    private static bool TryParseLine(string line, out string key, out List<string> values)
    {
        key = string.Empty;
        values = [];
        var tokens = Tokenize(line);
        if (tokens.Count == 0)
        {
            return false;
        }

        key = tokens[0];
        if (key is "proje" or "sürüm" or "hedef" or "başlangıç")
        {
            if (tokens.Count >= 2)
            {
                values.Add(Unquote(tokens[1]));
            }

            return true;
        }

        if (key == "paket")
        {
            if (tokens.Count >= 2)
            {
                values.Add(Unquote(tokens[1]));
            }

            if (tokens.Count >= 4 && tokens[2] == "sürüm")
            {
                values.Add(Unquote(tokens[3]));
            }
            else if (tokens.Count >= 3)
            {
                values.Add(Unquote(tokens[2]));
            }

            return true;
        }

        return false;
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < line.Length)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            if (index >= line.Length)
            {
                break;
            }

            if (line[index] == '"')
            {
                index++;
                var start = index;
                while (index < line.Length && line[index] != '"')
                {
                    index++;
                }

                tokens.Add(line[start..index]);
                if (index < line.Length && line[index] == '"')
                {
                    index++;
                }

                continue;
            }

            var begin = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index]))
            {
                index++;
            }

            tokens.Add(line[begin..index]);
        }

        return tokens;
    }

    private static string Unquote(string value) => value.Trim().Trim('"');

    private static Diagnostic Error(string path, string code, string message, int start, int length, int line, int column, string? suggestion) =>
        new(code, DiagnosticSeverity.Error, message, path, new TextSpan(start, length), line, column, suggestion);
}
