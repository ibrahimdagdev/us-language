namespace Us.Core;

public sealed class SourceText
{
    private readonly string _text;

    private SourceText(string text) => _text = text;

    public int Length => _text.Length;

    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _text[index];
        }
    }

    public static SourceText From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SourceText(text);
    }

    public ReadOnlySpan<char> Slice(TextSpan span)
    {
        EnsureSpan(span);
        return _text.AsSpan(span.Start, span.Length);
    }

    public string ToString(TextSpan span)
    {
        EnsureSpan(span);
        return _text.Substring(span.Start, span.Length);
    }

    public override string ToString() => _text;

    private void EnsureSpan(TextSpan span)
    {
        if (span.Start > _text.Length || span.End > _text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(span));
        }
    }
}
