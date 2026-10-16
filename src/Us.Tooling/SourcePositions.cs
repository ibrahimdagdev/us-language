using Us.Core;

namespace Us.Tooling;

/// <summary>
/// UTF-16 LSP positions (0-based line/character) mapped to Us source offsets.
/// .NET <see cref="string"/> and LSP both count UTF-16 code units.
/// </summary>
public readonly record struct LspPosition(int Line, int Character);

public readonly record struct LspRange(LspPosition Start, LspPosition End);

public sealed class SourcePositionMapper
{
    private readonly SourceText _text;
    private readonly LineMap _lines;

    public SourcePositionMapper(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        _lines = LineMap.From(text);
    }

    public SourceText Text => _text;

    public LineMap Lines => _lines;

    public int ToOffset(LspPosition position)
    {
        var line = Math.Clamp(position.Line + 1, 1, _lines.LineCount);
        var lineStart = _lines.GetLineStart(line);
        var lineEnd = line < _lines.LineCount ? _lines.GetLineStart(line + 1) : _text.Length;
        var character = Math.Clamp(position.Character, 0, Math.Max(0, lineEnd - lineStart));
        return Math.Clamp(lineStart + character, 0, _text.Length);
    }

    public LspPosition ToPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        var (line, column) = _lines.GetLineAndColumn(offset);
        return new LspPosition(line - 1, column - 1);
    }

    public LspRange ToRange(TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, _text.Length);
        var end = Math.Clamp(span.End, 0, _text.Length);
        return new LspRange(ToPosition(start), ToPosition(end));
    }

    public TextSpan ToSpan(LspRange range)
    {
        var start = ToOffset(range.Start);
        var end = ToOffset(range.End);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return TextSpan.FromBounds(start, end);
    }
}
