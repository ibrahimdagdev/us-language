namespace Us.Core;

public sealed record Diagnostic
{
    public Diagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        string? filePath,
        TextSpan span,
        int line,
        int column,
        string? suggestion)
    {
        if (!DiagnosticCodes.IsWellFormed(code))
        {
            throw new ArgumentException("Tanı kodu US ve dört rakam biçiminde olmalıdır.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Tanı iletisi boş olamaz.", nameof(message));
        }

        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        Code = code;
        Severity = severity;
        Message = message;
        FilePath = filePath;
        Span = span;
        Line = line;
        Column = column;
        Suggestion = suggestion;
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public string? FilePath { get; }

    public TextSpan Span { get; }

    public int Line { get; }

    public int Column { get; }

    public string? Suggestion { get; }
}
