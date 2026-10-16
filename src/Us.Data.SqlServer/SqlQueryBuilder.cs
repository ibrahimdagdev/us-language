using System.Text;
using Microsoft.Data.SqlClient;
using Us.Runtime;

namespace Us.Data.SqlServer;

/// <summary>Builds parameterized T-SQL from <see cref="UsQueryPlan"/> / CRUD shapes.</summary>
public static class SqlQueryBuilder
{
    public sealed class BuiltCommand
    {
        public required string Text { get; init; }

        public required List<SqlParameter> Parameters { get; init; }
    }

    public static BuiltCommand Select(UsTableSchema schema, UsQueryPlan plan)
    {
        var parameters = new List<SqlParameter>();
        var sql = new StringBuilder();
        sql.Append("SELECT ");
        var first = true;
        for (var i = 0; i < schema.FieldNames.Length; i++)
        {
            if (SqlTypeMap.IsRelationType(schema.FieldTypes[i]))
            {
                // FK stored under relation field name
            }

            if (!first)
            {
                sql.Append(", ");
            }

            first = false;
            SqlIdentifier.AppendQuoted(sql, schema.FieldNames[i]);
        }

        sql.Append(" FROM ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);

        if (plan.Filters.Count > 0)
        {
            sql.Append(" WHERE ");
            for (var i = 0; i < plan.Filters.Count; i++)
            {
                if (i > 0)
                {
                    sql.Append(" AND ");
                }

                sql.Append('(');
                AppendExpr(sql, plan.Filters[i], parameters, schema);
                sql.Append(')');
            }
        }

        if (plan.Sorts.Count > 0)
        {
            sql.Append(" ORDER BY ");
            for (var i = 0; i < plan.Sorts.Count; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                var sort = plan.Sorts[i];
                RequireColumn(schema, sort.FieldName);
                SqlIdentifier.AppendQuoted(sql, sort.FieldName);
                sql.Append(sort.Descending ? " DESC" : " ASC");
            }
        }

        return new BuiltCommand { Text = sql.ToString(), Parameters = parameters };
    }

    public static BuiltCommand Insert(UsTableSchema schema, UsEmitObject row, IReadOnlyDictionary<string, int> relations)
    {
        var parameters = new List<SqlParameter>();
        var columns = new StringBuilder();
        var values = new StringBuilder();
        var first = true;
        for (var i = 0; i < schema.FieldNames.Length; i++)
        {
            var name = schema.FieldNames[i];
            var type = schema.FieldTypes[i];
            object? raw;
            if (schema.Relations.ContainsKey(name))
            {
                if (!relations.TryGetValue(name, out var key))
                {
                    continue;
                }

                raw = key;
                type = "tamsayı";
            }
            else if (SqlTypeMap.IsRelationType(type))
            {
                continue;
            }
            else if (!row.IsAssigned(name) || !row.TryGetField(name, out var value))
            {
                continue;
            }
            else
            {
                raw = SqlTypeMap.ToParameterValue(value, type);
            }

            if (!first)
            {
                columns.Append(", ");
                values.Append(", ");
            }

            first = false;
            SqlIdentifier.AppendQuoted(columns, name);
            var pname = "@p" + parameters.Count;
            values.Append(pname);
            parameters.Add(CreateParameter(pname, type, raw));
        }

        var sql = new StringBuilder();
        sql.Append("INSERT INTO ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);
        sql.Append(" (").Append(columns).Append(") VALUES (").Append(values).Append(')');
        return new BuiltCommand { Text = sql.ToString(), Parameters = parameters };
    }

    public static BuiltCommand Update(UsTableSchema schema, UsEmitObject row, IReadOnlyDictionary<string, int> relations)
    {
        if (schema.KeyField is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", 0, 0);
        }

        if (!row.IsAssigned(schema.KeyField) || !row.TryGetField(schema.KeyField, out var keyValue))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", 0, 0);
        }

        var parameters = new List<SqlParameter>();
        var sql = new StringBuilder();
        sql.Append("UPDATE ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);
        sql.Append(" SET ");
        var first = true;
        for (var i = 0; i < schema.FieldNames.Length; i++)
        {
            var name = schema.FieldNames[i];
            if (string.Equals(name, schema.KeyField, StringComparison.Ordinal))
            {
                continue;
            }

            var type = schema.FieldTypes[i];
            object? raw;
            if (schema.Relations.ContainsKey(name))
            {
                if (!relations.TryGetValue(name, out var key))
                {
                    continue;
                }

                raw = key;
                type = "tamsayı";
            }
            else if (SqlTypeMap.IsRelationType(type))
            {
                continue;
            }
            else if (!row.IsAssigned(name) || !row.TryGetField(name, out var value))
            {
                continue;
            }
            else
            {
                raw = SqlTypeMap.ToParameterValue(value, type);
            }

            if (!first)
            {
                sql.Append(", ");
            }

            first = false;
            SqlIdentifier.AppendQuoted(sql, name);
            var pname = "@p" + parameters.Count;
            sql.Append(" = ").Append(pname);
            parameters.Add(CreateParameter(pname, type, raw));
        }

        var keyType = FieldType(schema, schema.KeyField);
        var keyParam = "@p" + parameters.Count;
        sql.Append(" WHERE ");
        SqlIdentifier.AppendQuoted(sql, schema.KeyField);
        sql.Append(" = ").Append(keyParam);
        parameters.Add(CreateParameter(keyParam, keyType, SqlTypeMap.ToParameterValue(keyValue, keyType)));
        return new BuiltCommand { Text = sql.ToString(), Parameters = parameters };
    }

    public static BuiltCommand Delete(UsTableSchema schema, UsEmitObject row)
    {
        if (schema.KeyField is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", 0, 0);
        }

        if (!row.IsAssigned(schema.KeyField) || !row.TryGetField(schema.KeyField, out var keyValue))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", 0, 0);
        }

        var keyType = FieldType(schema, schema.KeyField);
        var parameters = new List<SqlParameter>
        {
            CreateParameter("@p0", keyType, SqlTypeMap.ToParameterValue(keyValue, keyType)),
        };
        var sql = new StringBuilder();
        sql.Append("DELETE FROM ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);
        sql.Append(" WHERE ");
        SqlIdentifier.AppendQuoted(sql, schema.KeyField);
        sql.Append(" = @p0");
        return new BuiltCommand { Text = sql.ToString(), Parameters = parameters };
    }

    public static BuiltCommand SelectByKeys(UsTableSchema schema, string keyField, IReadOnlyList<int> keys)
    {
        RequireColumn(schema, keyField);
        var parameters = new List<SqlParameter>();
        var sql = new StringBuilder();
        sql.Append("SELECT ");
        for (var i = 0; i < schema.FieldNames.Length; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }

            SqlIdentifier.AppendQuoted(sql, schema.FieldNames[i]);
        }

        sql.Append(" FROM ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);
        sql.Append(" WHERE ");
        SqlIdentifier.AppendQuoted(sql, keyField);
        sql.Append(" IN (");
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }

            var pname = "@p" + i;
            sql.Append(pname);
            parameters.Add(CreateParameter(pname, "tamsayı", keys[i]));
        }

        sql.Append(')');
        return new BuiltCommand { Text = sql.ToString(), Parameters = parameters };
    }

    public static BuiltCommand NextKey(UsTableSchema schema, string keyField)
    {
        RequireColumn(schema, keyField);
        var sql = new StringBuilder();
        sql.Append("SELECT ISNULL(MAX(");
        SqlIdentifier.AppendQuoted(sql, keyField);
        sql.Append("), 0) + 1 FROM ");
        SqlIdentifier.AppendQuoted(sql, schema.Name);
        return new BuiltCommand { Text = sql.ToString(), Parameters = [] };
    }

    private static void AppendExpr(StringBuilder sql, UsQueryExpr expr, List<SqlParameter> parameters, UsTableSchema schema)
    {
        switch (expr)
        {
            case UsQueryExpr.FieldExpr field:
                RequireColumn(schema, field.Name);
                SqlIdentifier.AppendQuoted(sql, field.Name);
                break;
            case UsQueryExpr.ConstantExpr constant:
            {
                var pname = "@p" + parameters.Count;
                sql.Append(pname);
                parameters.Add(CreateParameter(pname, InferType(constant.Value), ToRaw(constant.Value)));
                break;
            }
            case UsQueryExpr.CompareExpr compare:
                if (compare.Op is "==" or "!="
                    && (IsNullConstant(compare.Left) || IsNullConstant(compare.Right)))
                {
                    var field = compare.Left as UsQueryExpr.FieldExpr ?? compare.Right as UsQueryExpr.FieldExpr
                        ?? throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "NULL karşılaştırma alanı yok.", 0, 0);
                    RequireColumn(schema, field.Name);
                    SqlIdentifier.AppendQuoted(sql, field.Name);
                    sql.Append(compare.Op == "==" ? " IS NULL" : " IS NOT NULL");
                    break;
                }

                AppendExpr(sql, compare.Left, parameters, schema);
                sql.Append(' ').Append(MapOp(compare.Op)).Append(' ');
                AppendExpr(sql, compare.Right, parameters, schema);
                break;
            case UsQueryExpr.AndExpr and:
                sql.Append('(');
                AppendExpr(sql, and.Left, parameters, schema);
                sql.Append(" AND ");
                AppendExpr(sql, and.Right, parameters, schema);
                sql.Append(')');
                break;
            case UsQueryExpr.OrExpr or:
                sql.Append('(');
                AppendExpr(sql, or.Left, parameters, schema);
                sql.Append(" OR ");
                AppendExpr(sql, or.Right, parameters, schema);
                sql.Append(')');
                break;
            case UsQueryExpr.NotExpr not:
                sql.Append("NOT (");
                AppendExpr(sql, not.Operand, parameters, schema);
                sql.Append(')');
                break;
            case UsQueryExpr.TruthyExpr truthy:
                AppendExpr(sql, truthy.Operand, parameters, schema);
                sql.Append(" = ");
                var tname = "@p" + parameters.Count;
                sql.Append(tname);
                parameters.Add(CreateParameter(tname, "mantıksal", true));
                break;
            default:
                throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, "Desteklenmeyen sorgu ifadesi.", 0, 0);
        }
    }

    private static bool IsNullConstant(UsQueryExpr expr) =>
        expr is UsQueryExpr.ConstantExpr constant && constant.Value.IsNull;

    private static string MapOp(string op) => op switch
    {
        "==" => "=",
        "!=" => "<>",
        ">" => ">",
        ">=" => ">=",
        "<" => "<",
        "<=" => "<=",
        _ => throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"Desteklenmeyen karşılaştırma '{op}'.", 0, 0),
    };

    private static void RequireColumn(UsTableSchema schema, string name)
    {
        if (!schema.FieldNames.Contains(name, StringComparer.Ordinal))
        {
            throw new UsRuntimeException(UsRuntimeCodes.NotExecutable, $"'{name}' sütunu yok.", 0, 0);
        }
    }

    private static string FieldType(UsTableSchema schema, string name)
    {
        var index = Array.FindIndex(schema.FieldNames, item => string.Equals(item, name, StringComparison.Ordinal));
        return index >= 0 ? schema.FieldTypes[index] : "tamsayı";
    }

    private static string InferType(UsValue value) => value.Kind switch
    {
        UsValueKind.Bool => "mantıksal",
        UsValueKind.Int => "tamsayı",
        UsValueKind.UInt => "utamsayı",
        UsValueKind.Float64 => "ondalık",
        UsValueKind.Float32 => "gondalık",
        UsValueKind.Decimal => "hondalık",
        UsValueKind.Char => "karakter",
        UsValueKind.Text => "metin",
        UsValueKind.Null => "metin",
        _ => "metin",
    };

    private static object ToRaw(UsValue value) =>
        value.IsNull ? DBNull.Value : SqlTypeMap.ToParameterValue(value, InferType(value));

    private static SqlParameter CreateParameter(string name, string usType, object? value)
    {
        var parameter = new SqlParameter(name, SqlTypeMap.DbType(usType))
        {
            Value = value ?? DBNull.Value,
        };
        if (SqlTypeMap.DbType(usType) == System.Data.SqlDbType.Decimal)
        {
            parameter.Precision = 28;
            parameter.Scale = 10;
        }

        return parameter;
    }
}
