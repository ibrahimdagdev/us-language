using Us.Core;

namespace Us.Cli;

public static class DiagnosticWriter
{
    public static void Write(TextWriter writer, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteLine($"{SeverityLabel(diagnostic.Severity)} {diagnostic.Code}");
            writer.WriteLine();
            writer.WriteLine(diagnostic.Message);
            writer.WriteLine();
            if (!string.IsNullOrEmpty(diagnostic.FilePath))
            {
                writer.WriteLine($"{diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column}");
            }

            if (!string.IsNullOrEmpty(diagnostic.Suggestion))
            {
                writer.WriteLine();
                writer.WriteLine("Öneri:");
                writer.WriteLine(diagnostic.Suggestion);
            }

            writer.WriteLine();
        }
    }

    private static string SeverityLabel(DiagnosticSeverity severity) =>
        severity switch
        {
            DiagnosticSeverity.Error => "Hata",
            DiagnosticSeverity.Warning => "Uyarı",
            _ => "Bilgi",
        };
}
