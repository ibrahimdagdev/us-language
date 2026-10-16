using Microsoft.Data.SqlClient;
using Us.Core;
using Us.Runtime;

namespace Us.Data.SqlServer;

public sealed record SqlSchemaColumn(string Name, string UsType, bool Nullable);

public sealed record SqlSchemaTable(string Name, IReadOnlyList<SqlSchemaColumn> Columns);

/// <summary>Option C phased schema: inspect + additive DDL; destructive requires onay (US1550).</summary>
public static class SqlSchemaSync
{
    public static IReadOnlyList<SqlSchemaTable> Inspect(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var tables = new List<SqlSchemaTable>();
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.name AS TableName, c.name AS ColumnName, ty.name AS TypeName, c.is_nullable
            FROM sys.tables t
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.is_ms_shipped = 0
            ORDER BY t.name, c.column_id
            """;
        using var reader = command.ExecuteReader();
        string? current = null;
        var columns = new List<SqlSchemaColumn>();
        while (reader.Read())
        {
            var table = reader.GetString(0);
            if (current is not null && !string.Equals(current, table, StringComparison.Ordinal))
            {
                tables.Add(new SqlSchemaTable(current, columns));
                columns = [];
            }

            current = table;
            columns.Add(new SqlSchemaColumn(
                reader.GetString(1),
                FromSqlType(reader.GetString(2)),
                reader.GetBoolean(3)));
        }

        if (current is not null)
        {
            tables.Add(new SqlSchemaTable(current, columns));
        }

        return tables;
    }

    public static SqlSchemaApplyResult Apply(
        string connectionString,
        IReadOnlyList<UsTableSchema> desired,
        bool approve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var diagnostics = new List<Diagnostic>();
        var actual = Inspect(connectionString);
        var steps = Diff(desired, actual);
        if (steps.Count == 0)
        {
            return new SqlSchemaApplyResult(steps, diagnostics, false);
        }

        if (steps.Any(IsDestructive) && !approve)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.DestructiveSchema,
                DiagnosticSeverity.Error,
                "Yıkıcı şema değişikliği onay olmadan uygulanmaz.",
                null,
                default,
                1,
                1,
                null));
            return new SqlSchemaApplyResult(steps, diagnostics, false);
        }

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        foreach (var step in steps)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Render(step, desired);
            command.ExecuteNonQuery();
        }

        return new SqlSchemaApplyResult(steps, diagnostics, true);
    }

    /// <summary>Applies when <c>US_TEST_SQLSERVER_CONNECTION</c> / <c>US_SQLSERVER_CONNECTION</c> is set.</summary>
    public static SqlSchemaApplyResult? TryApplyFromEnvironment(IReadOnlyList<UsTableSchema> desired, bool approve)
    {
        var connection = Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION")
            ?? Environment.GetEnvironmentVariable("US_SQLSERVER_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            return null;
        }

        return Apply(connection, desired, approve);
    }

    private static List<SqlSchemaStep> Diff(IReadOnlyList<UsTableSchema> desired, IReadOnlyList<SqlSchemaTable> actual)
    {
        var steps = new List<SqlSchemaStep>();
        foreach (var table in desired)
        {
            var current = actual.FirstOrDefault(item => string.Equals(item.Name, table.Name, StringComparison.Ordinal));
            if (current is null)
            {
                steps.Add(new SqlSchemaStep(SqlSchemaStepKind.CreateTable, table.Name, string.Empty, string.Empty, string.Empty));
                continue;
            }

            for (var i = 0; i < table.FieldNames.Length; i++)
            {
                var name = table.FieldNames[i];
                var type = table.FieldTypes[i];
                var found = current.Columns.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
                if (found is null)
                {
                    steps.Add(new SqlSchemaStep(SqlSchemaStepKind.AddColumn, table.Name, name, string.Empty, type));
                }
                else if (!string.Equals(SqlTypeMap.StripNullable(found.UsType), SqlTypeMap.StripNullable(type), StringComparison.Ordinal)
                    && !SqlTypeMap.IsRelationType(type))
                {
                    steps.Add(new SqlSchemaStep(SqlSchemaStepKind.NarrowColumn, table.Name, name, found.UsType, type));
                }
            }

            foreach (var column in current.Columns)
            {
                if (table.FieldNames.All(item => !string.Equals(item, column.Name, StringComparison.Ordinal)))
                {
                    steps.Add(new SqlSchemaStep(SqlSchemaStepKind.DropColumn, table.Name, column.Name, column.UsType, string.Empty));
                }
            }
        }

        foreach (var table in actual)
        {
            if (desired.All(item => !string.Equals(item.Name, table.Name, StringComparison.Ordinal)))
            {
                steps.Add(new SqlSchemaStep(SqlSchemaStepKind.DropTable, table.Name, string.Empty, string.Empty, string.Empty));
            }
        }

        return steps;
    }

    private static bool IsDestructive(SqlSchemaStep step) =>
        step.Kind is SqlSchemaStepKind.DropColumn or SqlSchemaStepKind.DropTable or SqlSchemaStepKind.NarrowColumn;

    private static string Render(SqlSchemaStep step, IReadOnlyList<UsTableSchema> desired)
    {
        return step.Kind switch
        {
            SqlSchemaStepKind.CreateTable => RenderCreate(desired.First(item => item.Name == step.Table)),
            SqlSchemaStepKind.AddColumn =>
                "ALTER TABLE " + SqlIdentifier.Quote(step.Table) + " ADD " + SqlIdentifier.Quote(step.Column) + " "
                + SqlTypeMap.SqlType(step.To, nullable: true),
            SqlSchemaStepKind.DropColumn =>
                "ALTER TABLE " + SqlIdentifier.Quote(step.Table) + " DROP COLUMN " + SqlIdentifier.Quote(step.Column),
            SqlSchemaStepKind.DropTable => "DROP TABLE " + SqlIdentifier.Quote(step.Table),
            SqlSchemaStepKind.NarrowColumn =>
                "ALTER TABLE " + SqlIdentifier.Quote(step.Table) + " ALTER COLUMN " + SqlIdentifier.Quote(step.Column) + " "
                + SqlTypeMap.SqlType(step.To, nullable: true),
            _ => throw new InvalidOperationException(step.Kind.ToString()),
        };
    }

    private static string RenderCreate(UsTableSchema table)
    {
        var parts = new List<string>();
        for (var i = 0; i < table.FieldNames.Length; i++)
        {
            var name = table.FieldNames[i];
            var type = table.FieldTypes[i];
            var nullable = i < table.Nullable.Length && table.Nullable[i];
            if (SqlTypeMap.IsRelationType(type) || table.Relations.ContainsKey(name))
            {
                type = "tamsayı";
                nullable = true;
            }

            var sqlType = SqlTypeMap.SqlType(type, nullable || (table.KeyField is not null && name == table.KeyField));
            // key is NOT NULL but assigned by app — keep NOT NULL for key
            if (table.KeyField is not null && name == table.KeyField)
            {
                sqlType = SqlTypeMap.SqlType("tamsayı", nullable: false);
            }

            parts.Add(SqlIdentifier.Quote(name) + " " + sqlType);
        }

        return "CREATE TABLE " + SqlIdentifier.Quote(table.Name) + " (" + string.Join(", ", parts) + ")";
    }

    private static string FromSqlType(string sql) => sql.ToLowerInvariant() switch
    {
        "nvarchar" or "varchar" or "nchar" or "char" or "text" or "ntext" => "metin",
        "int" => "tamsayı",
        "bigint" => "utamsayı",
        "float" => "ondalık",
        "real" => "gondalık",
        "decimal" or "numeric" => "hondalık",
        "bit" => "mantıksal",
        "date" => "tarih",
        "time" => "saat",
        "datetime" or "datetime2" or "smalldatetime" => "tarihsaat",
        _ => sql,
    };
}

public enum SqlSchemaStepKind
{
    CreateTable,
    AddColumn,
    DropColumn,
    DropTable,
    NarrowColumn,
}

public sealed record SqlSchemaStep(SqlSchemaStepKind Kind, string Table, string Column, string From, string To);

public sealed class SqlSchemaApplyResult
{
    public SqlSchemaApplyResult(IReadOnlyList<SqlSchemaStep> steps, IReadOnlyList<Diagnostic> diagnostics, bool applied)
    {
        Steps = steps;
        Diagnostics = diagnostics;
        Applied = applied;
    }

    public IReadOnlyList<SqlSchemaStep> Steps { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool Applied { get; }
}
