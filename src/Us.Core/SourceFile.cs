using System.Text;

namespace Us.Core;

public readonly record struct EncodingFault(int ByteOffset, int TextOffset);

public sealed class SourceFile
{
    private SourceFile(string path, SourceText text, LineMap lines, IReadOnlyList<EncodingFault> encodingFaults)
    {
        Path = path;
        Text = text;
        Lines = lines;
        EncodingFaults = encodingFaults;
    }

    public string Path { get; }

    public SourceText Text { get; }

    public LineMap Lines { get; }

    public IReadOnlyList<EncodingFault> EncodingFaults { get; }

    public static SourceFile FromText(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = SourceText.From(text);
        return new SourceFile(path, source, LineMap.From(source), []);
    }

    public static SourceFile FromUtf8(string path, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var start = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            start = 3;
        }

        var faults = new List<EncodingFault>();
        var builder = new StringBuilder();
        var i = start;
        while (i < bytes.Length)
        {
            if (TryDecode(bytes, ref i, out var codePoint))
            {
                builder.Append(char.ConvertFromUtf32(codePoint));
                continue;
            }

            faults.Add(new EncodingFault(i, builder.Length));
            builder.Append('\uFFFD');
            i++;
        }

        var text = SourceText.From(builder.ToString());
        return new SourceFile(path, text, LineMap.From(text), faults);
    }

    public TextLocation GetLocation(TextSpan span)
    {
        var (line, column) = Lines.GetLineAndColumn(span.Start);
        return new TextLocation(Path, span, line, column);
    }

    public IReadOnlyList<Diagnostic> GetEncodingDiagnostics()
    {
        var diagnostics = new List<Diagnostic>(EncodingFaults.Count);
        foreach (var fault in EncodingFaults)
        {
            var span = new TextSpan(fault.TextOffset, 1);
            var (line, column) = Lines.GetLineAndColumn(fault.TextOffset);
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.InvalidUtf8,
                DiagnosticSeverity.Error,
                "Kaynak UTF-8 olarak okunamadı. Bozuk bayt dizisi değiştirme karakteriyle korundu.",
                Path,
                span,
                line,
                column,
                suggestion: null));
        }

        return diagnostics;
    }

    private static bool TryDecode(ReadOnlySpan<byte> bytes, ref int index, out int codePoint)
    {
        var first = bytes[index];
        int need;
        int min;
        if (first <= 0x7F)
        {
            codePoint = first;
            index++;
            return true;
        }

        if (first >= 0xC2 && first <= 0xDF)
        {
            need = 1;
            codePoint = first & 0x1F;
            min = 0x80;
        }
        else if (first >= 0xE0 && first <= 0xEF)
        {
            need = 2;
            codePoint = first & 0x0F;
            min = 0x800;
        }
        else if (first >= 0xF0 && first <= 0xF4)
        {
            need = 3;
            codePoint = first & 0x07;
            min = 0x10000;
        }
        else
        {
            codePoint = 0;
            return false;
        }

        if (index + need >= bytes.Length)
        {
            codePoint = 0;
            return false;
        }

        for (var n = 1; n <= need; n++)
        {
            var next = bytes[index + n];
            if ((next & 0xC0) != 0x80)
            {
                codePoint = 0;
                return false;
            }

            codePoint = (codePoint << 6) | (next & 0x3F);
        }

        if (codePoint < min || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            codePoint = 0;
            return false;
        }

        index += need + 1;
        return true;
    }
}
