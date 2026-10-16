using System.Text.Json;
using Us.Core;
using Us.Data.SqlServer;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public enum SchemaStepKind
{
    CreateTable,
    AddColumn,
    DropColumn,
    DropTable,
    NarrowColumn,
}

public sealed record SchemaStep(SchemaStepKind Kind, string Table, string Column, string From, string To);

public sealed class SchemaSyncResult
{
    public SchemaSyncResult(IReadOnlyList<SchemaStep> steps, IReadOnlyList<Diagnostic> diagnostics, bool applied)
    {
        Steps = steps;
        Diagnostics = diagnostics;
        Applied = applied;
    }

    public IReadOnlyList<SchemaStep> Steps { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public bool Applied { get; }
}

public sealed class SchemaColumnRecord
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

public sealed class SchemaTableRecord
{
    public string Name { get; set; } = string.Empty;

    public List<SchemaColumnRecord> Columns { get; set; } = [];
}

public sealed class SchemaHistoryRecord
{
    public string Kind { get; set; } = string.Empty;

    public string Table { get; set; } = string.Empty;

    public string Column { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}

public sealed class SchemaDocument
{
    public List<SchemaTableRecord> Tables { get; set; } = [];

    public List<SchemaHistoryRecord> History { get; set; } = [];
}

public static class SyncDatabase
{
    public static string FilePath(string root) => Path.Combine(root, ".us-schema.json");

    public static SchemaSyncResult Run(string root, bool approve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var diagnostics = new List<Diagnostic>();
        var desired = ReadDesired(root, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new SchemaSyncResult([], diagnostics, false);
        }

        var document = ReadActual(root, diagnostics);
        if (diagnostics.Count > 0)
        {
            return new SchemaSyncResult([], diagnostics, false);
        }

        var steps = Diff(desired, document.Tables);
        var applied = false;
        if (steps.Count > 0)
        {
            if (steps.Any(IsDestructive) && !approve)
            {
                diagnostics.Add(SchemaDiagnostic(root, DiagnosticCodes.DestructiveSchema, "Yıkıcı şema değişikliği onay olmadan uygulanmaz."));
                return new SchemaSyncResult(steps, diagnostics, false);
            }

            Apply(document, desired, steps);
            Write(root, document);
            applied = true;
        }

        // Option C: when SQL connection is configured, inspect/apply against SQL Server as well.
        var sqlResult = SqlSchemaSync.TryApplyFromEnvironment(ToSqlSchemas(desired), approve);
        if (sqlResult is not null)
        {
            diagnostics.AddRange(sqlResult.Diagnostics);
            if (sqlResult.Diagnostics.Any(item => item.Code == DiagnosticCodes.DestructiveSchema))
            {
                return new SchemaSyncResult(steps, diagnostics, false);
            }

            applied = applied || sqlResult.Applied;
        }

        return new SchemaSyncResult(steps, diagnostics, applied);
    }

    private static List<UsTableSchema> ToSqlSchemas(List<SchemaTableRecord> desired)
    {
        var list = new List<UsTableSchema>();
        foreach (var table in desired)
        {
            list.Add(new UsTableSchema
            {
                Name = table.Name,
                ModelName = table.Name,
                FieldNames = table.Columns.Select(column => column.Name).ToArray(),
                FieldTypes = table.Columns.Select(column => column.Type).ToArray(),
                Nullable = table.Columns.Select(column => column.Type.EndsWith('?')).ToArray(),
                Required = table.Columns.Select(column => !column.Type.EndsWith('?')).ToArray(),
                KeyField = table.Columns.FirstOrDefault(column => column.Name == "id")?.Name,
            });
        }

        return list;
    }

    private static List<SchemaTableRecord> ReadDesired(string root, List<Diagnostic> diagnostics)
    {
        var tables = new List<SchemaTableRecord>();
        if (!Directory.Exists(root))
        {
            return tables;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.us", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
        {
            var compiled = UsCompile.Front(SourceFile.FromUtf8(path, File.ReadAllBytes(path)));
            if (compiled.Errors.Length > 0)
            {
                diagnostics.AddRange(compiled.Errors);
                return [];
            }

            foreach (var database in compiled.Compilation.Databases)
            {
                foreach (var table in database.Tables)
                {
                    if (table.Model is not SourceTypeSymbol model)
                    {
                        continue;
                    }

                    var record = new SchemaTableRecord { Name = table.Name };
                    foreach (var field in model.Definition.Fields)
                    {
                        record.Columns.Add(new SchemaColumnRecord { Name = field.Name, Type = field.Type.DisplayName });
                    }

                    var existing = tables.FindIndex(item => item.Name == record.Name);
                    if (existing >= 0)
                    {
                        tables[existing] = record;
                    }
                    else
                    {
                        tables.Add(record);
                    }
                }
            }
        }

        return tables;
    }

    private static SchemaDocument ReadActual(string root, List<Diagnostic> diagnostics)
    {
        var path = FilePath(root);
        if (!File.Exists(path))
        {
            return new SchemaDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(path)) ?? new SchemaDocument();
        }
        catch (JsonException)
        {
            diagnostics.Add(SchemaDiagnostic(root, DiagnosticCodes.JsonMalformed, "Şema geçmişi okunamadı."));
            return new SchemaDocument();
        }
    }

    private static List<SchemaStep> Diff(List<SchemaTableRecord> desired, List<SchemaTableRecord> actual)
    {
        var steps = new List<SchemaStep>();
        foreach (var table in desired)
        {
            var current = actual.FirstOrDefault(item => item.Name == table.Name);
            if (current is null)
            {
                steps.Add(new SchemaStep(SchemaStepKind.CreateTable, table.Name, string.Empty, string.Empty, string.Empty));
                continue;
            }

            foreach (var column in table.Columns)
            {
                var found = current.Columns.FirstOrDefault(item => item.Name == column.Name);
                if (found is null)
                {
                    steps.Add(new SchemaStep(SchemaStepKind.AddColumn, table.Name, column.Name, string.Empty, column.Type));
                }
                else if (!string.Equals(found.Type, column.Type, StringComparison.Ordinal))
                {
                    steps.Add(new SchemaStep(SchemaStepKind.NarrowColumn, table.Name, column.Name, found.Type, column.Type));
                }
            }

            foreach (var column in current.Columns)
            {
                if (table.Columns.All(item => item.Name != column.Name))
                {
                    steps.Add(new SchemaStep(SchemaStepKind.DropColumn, table.Name, column.Name, column.Type, string.Empty));
                }
            }
        }

        foreach (var table in actual)
        {
            if (desired.All(item => item.Name != table.Name))
            {
                steps.Add(new SchemaStep(SchemaStepKind.DropTable, table.Name, string.Empty, string.Empty, string.Empty));
            }
        }

        return steps;
    }

    private static bool IsDestructive(SchemaStep step) =>
        step.Kind is SchemaStepKind.DropColumn or SchemaStepKind.DropTable or SchemaStepKind.NarrowColumn;

    private static void Apply(SchemaDocument document, List<SchemaTableRecord> desired, List<SchemaStep> steps)
    {
        foreach (var step in steps)
        {
            var note = IsDestructive(step)
                ? "Geri alma notu: yıkıcı değişiklik onaylandı."
                : "Geri alma notu: adım güvenlidir.";
            document.History.Add(new SchemaHistoryRecord
            {
                Kind = step.Kind.ToString(),
                Table = step.Table,
                Column = step.Column,
                Note = note,
            });
        }

        document.Tables = desired.Select(table => new SchemaTableRecord
        {
            Name = table.Name,
            Columns = table.Columns.Select(column => new SchemaColumnRecord { Name = column.Name, Type = column.Type }).ToList(),
        }).ToList();
    }

    private static void Write(string root, SchemaDocument document)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(FilePath(root), JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Diagnostic SchemaDiagnostic(string root, string code, string message) =>
        new(code, DiagnosticSeverity.Error, message, FilePath(root), new TextSpan(0, 1), 1, 1, null);
}
