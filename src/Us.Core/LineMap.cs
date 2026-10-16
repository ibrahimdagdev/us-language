namespace Us.Core;

public sealed class LineMap
{
    private readonly int[] _starts;
    private readonly int _length;

    private LineMap(int[] starts, int length)
    {
        _starts = starts;
        _length = length;
    }

    public int LineCount => _starts.Length;

    public static LineMap From(SourceText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                starts.Add(i + 1);
            }
            else if (ch == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return new LineMap(starts.ToArray(), text.Length);
    }

    public int GetLineStart(int line)
    {
        if (line < 1 || line > _starts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(line));
        }

        return _starts[line - 1];
    }

    public (int Line, int Column) GetLineAndColumn(int offset)
    {
        if (offset < 0 || offset > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var index = Array.BinarySearch(_starts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        var lineStart = _starts[index];
        return (index + 1, offset - lineStart + 1);
    }
}
