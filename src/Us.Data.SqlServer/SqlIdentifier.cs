using System.Text;

namespace Us.Data.SqlServer;

/// <summary>Quotes SQL Server identifiers from trusted schema metadata only.</summary>
public static class SqlIdentifier
{
    public static string Quote(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    public static string Qualify(string table, string column) => Quote(table) + "." + Quote(column);

    public static void AppendQuoted(StringBuilder builder, string name)
    {
        builder.Append(Quote(name));
    }
}
