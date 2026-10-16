using System.Text;
using Us.Lexer;

namespace Us.Parser;

public static class SyntaxDumper
{
    public static string Dump(SyntaxNode node)
    {
        var builder = new StringBuilder();
        Write(node, builder, 0);
        return builder.ToString();
    }

    private static void Write(SyntaxNode node, StringBuilder builder, int indent)
    {
        builder.Append(' ', indent * 2);
        builder.Append(node.Kind);
        if (node is SyntaxToken token)
        {
            builder.Append(' ');
            builder.Append(token.TokenKind);
            if (token.IsMissing)
            {
                builder.Append(" eksik");
            }
        }

        builder.AppendLine();
        foreach (var child in node.Children)
        {
            Write(child, builder, indent + 1);
        }
    }
}
