using Us.Core;

namespace Us.Lexer;

public static class LiteralText
{
    public static string Decode(SourceText text, TextSpan span, bool trimQuotes)
    {
        var raw = text.ToString(span);
        var start = 0;
        var end = raw.Length;
        if (trimQuotes && end > 0 && raw[0] == '"')
        {
            start = 1;
        }

        if (trimQuotes && end - start > 0 && raw[end - 1] == '"')
        {
            end--;
        }

        var builder = new System.Text.StringBuilder();
        for (var i = start; i < end; i++)
        {
            var ch = raw[i];
            if (ch == '\\' && i + 1 < end)
            {
                builder.Append(DecodeEscape(raw[i + 1]));
                i++;
                continue;
            }

            if (ch == '{' && i + 1 < end && raw[i + 1] == '{')
            {
                builder.Append('{');
                i++;
                continue;
            }

            if (ch == '}' && i + 1 < end && raw[i + 1] == '}')
            {
                builder.Append('}');
                i++;
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static char DecodeEscape(char escaped) => escaped switch
    {
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        '\\' => '\\',
        '"' => '"',
        '\'' => '\'',
        _ => escaped,
    };
}
