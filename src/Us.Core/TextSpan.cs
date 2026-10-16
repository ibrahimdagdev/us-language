namespace Us.Core;

public readonly record struct TextSpan
{
    public TextSpan(int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => Start + Length;

    public static TextSpan FromBounds(int start, int end)
    {
        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        return new TextSpan(start, end - start);
    }

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextSpan other) => other.Start >= Start && other.End <= End;

    public bool Overlaps(TextSpan other)
    {
        var start = Math.Max(Start, other.Start);
        var end = Math.Min(End, other.End);
        return start < end;
    }
}
