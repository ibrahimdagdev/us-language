namespace Us.Core;

public readonly record struct TextLocation
{
    public TextLocation(string filePath, TextSpan span, int line, int column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (line < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        FilePath = filePath;
        Span = span;
        Line = line;
        Column = column;
    }

    public string FilePath { get; }

    public TextSpan Span { get; }

    public int Line { get; }

    public int Column { get; }
}
