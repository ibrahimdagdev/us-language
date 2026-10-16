using Microsoft.Data.SqlClient;
using Us.Runtime;

namespace Us.Data.SqlServer;

/// <summary>SQL Server implementation of <see cref="IUsDatabaseBackend"/>.</summary>
public sealed class SqlServerBackend : IUsDatabaseBackend, IAsyncDisposable, IDisposable
{
    private readonly string _connectionString;
    private readonly Dictionary<string, UsTableSchema> _tables = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private int _tx; // 0 none, 1 active, 2 closed

    public SqlServerBackend(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public void Reset()
    {
        lock (_sync)
        {
            DisposeSession();
            _tables.Clear();
            _tx = 0;
        }
    }

    public void RegisterTable(UsTableSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (_sync)
        {
            _tables[schema.Name] = schema;
        }
    }

    public void RegisterRelation(string tableName, string fieldName, string relatedTableName, string relatedKeyField)
    {
        lock (_sync)
        {
            if (!_tables.TryGetValue(tableName, out var schema))
            {
                throw new InvalidOperationException($"'{tableName}' tablosu kayıtlı değil.");
            }

            schema.Relations[fieldName] = new UsRelationSchema
            {
                FieldName = fieldName,
                RelatedTableName = relatedTableName,
                RelatedKeyField = relatedKeyField,
            };
        }
    }

    public void Begin()
    {
        lock (_sync)
        {
            if (_tx == 1)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem zaten açık.", 0, 0);
            }

            EnsureConnection();
            _transaction = _connection!.BeginTransaction();
            _tx = 1;
        }
    }

    public void Commit()
    {
        lock (_sync)
        {
            if (_tx != 1 || _transaction is null)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
            }

            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;
            _tx = 2;
        }
    }

    public void Rollback()
    {
        lock (_sync)
        {
            if (_tx != 1 || _transaction is null)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
            }

            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;
            _tx = 2;
        }
    }

    public void Finish(bool commit)
    {
        lock (_sync)
        {
            if (_tx != 1)
            {
                return;
            }
        }

        if (commit)
        {
            Commit();
        }
        else
        {
            Rollback();
        }
    }

    public int NextKey(string tableName, string keyField)
    {
        var schema = Require(tableName);
        var built = SqlQueryBuilder.NextKey(schema, keyField);
        using var command = CreateCommand(built);
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result);
    }

    public void Insert(string tableName, UsEmitObject row, IReadOnlyDictionary<string, int> relations, int start, int length)
    {
        try
        {
            var schema = Require(tableName);
            var built = SqlQueryBuilder.Insert(schema, row, relations);
            using var command = CreateCommand(built);
            command.ExecuteNonQuery();
        }
        catch (UsRuntimeException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw Translate(exception, start, length);
        }
    }

    public void Update(string tableName, UsEmitObject row, IReadOnlyDictionary<string, int> relations, int start, int length)
    {
        try
        {
            var schema = Require(tableName);
            var built = SqlQueryBuilder.Update(schema, row, relations);
            using var command = CreateCommand(built);
            var affected = command.ExecuteNonQuery();
            if (affected == 0)
            {
                throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", start, length);
            }
        }
        catch (UsRuntimeException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw Translate(exception, start, length);
        }
    }

    public void Delete(string tableName, UsEmitObject row, int start, int length)
    {
        try
        {
            var schema = Require(tableName);
            var built = SqlQueryBuilder.Delete(schema, row);
            using var command = CreateCommand(built);
            var affected = command.ExecuteNonQuery();
            if (affected == 0)
            {
                throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", start, length);
            }
        }
        catch (UsRuntimeException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw Translate(exception, start, length);
        }
    }

    public IReadOnlyList<UsEmitObject> Query(UsQueryPlan plan, int start, int length)
    {
        try
        {
            var schema = Require(plan.TableName);
            var built = SqlQueryBuilder.Select(schema, plan);
            using var command = CreateCommand(built);
            using var reader = command.ExecuteReader();
            var rows = new List<UsEmitObject>();
            var relationKeys = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            while (reader.Read())
            {
                var instance = MaterializeRow(schema, reader);
                rows.Add(instance);
                foreach (var include in plan.Includes)
                {
                    if (schema.Relations.TryGetValue(include, out var relation)
                        && instance.TryGetField(include, out var fk)
                        && fk.Kind == UsValueKind.Int)
                    {
                        // FK temporarily stored as int field before hydrate rewrite
                        if (!relationKeys.TryGetValue(include, out var list))
                        {
                            list = [];
                            relationKeys[include] = list;
                        }

                        if (!list.Contains(fk.Int))
                        {
                            list.Add(fk.Int);
                        }
                    }
                }
            }

            reader.Close();

            foreach (var include in plan.Includes)
            {
                if (!schema.Relations.TryGetValue(include, out var relation))
                {
                    throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{include}' için tablo yok.", start, length);
                }

                if (!relationKeys.TryGetValue(include, out var keys) || keys.Count == 0)
                {
                    foreach (var row in rows)
                    {
                        // leave unassigned — clear any raw FK int if present
                        ClearField(row, include);
                    }

                    continue;
                }

                var relatedSchema = Require(relation.RelatedTableName);
                var relatedBuilt = SqlQueryBuilder.SelectByKeys(relatedSchema, relation.RelatedKeyField, keys);
                using var relatedCommand = CreateCommand(relatedBuilt);
                using var relatedReader = relatedCommand.ExecuteReader();
                var relatedByKey = new Dictionary<int, UsEmitObject>();
                while (relatedReader.Read())
                {
                    var related = MaterializeRow(relatedSchema, relatedReader);
                    if (related.TryGetField(relation.RelatedKeyField, out var key) && key.Kind == UsValueKind.Int)
                    {
                        relatedByKey[key.Int] = related;
                    }
                }

                foreach (var row in rows)
                {
                    if (row.TryGetField(include, out var fk) && fk.Kind == UsValueKind.Int && relatedByKey.TryGetValue(fk.Int, out var related))
                    {
                        UsEmitObject.WriteField(UsValue.FromReference(row), include, UsValue.FromReference(related.Clone()), start, length);
                    }
                    else
                    {
                        ClearField(row, include);
                    }
                }
            }

            return rows;
        }
        catch (UsRuntimeException)
        {
            throw;
        }
        catch (SqlException exception)
        {
            throw Translate(exception, start, length);
        }
    }

    private UsEmitObject MaterializeRow(UsTableSchema schema, SqlDataReader reader)
    {
        var instance = new UsEmitObject(schema.ModelName, schema.FieldNames, schema.Nullable);
        for (var i = 0; i < schema.FieldNames.Length; i++)
        {
            var name = schema.FieldNames[i];
            var type = schema.FieldTypes[i];
            var nullable = i < schema.Nullable.Length && schema.Nullable[i];
            if (SqlTypeMap.IsRelationType(type) || schema.Relations.ContainsKey(name))
            {
                // Read FK int into the field temporarily for include hydration
                if (!reader.IsDBNull(i))
                {
                    UsEmitObject.WriteField(
                        UsValue.FromReference(instance),
                        name,
                        UsValue.FromInt(reader.GetInt32(i)),
                        0,
                        0);
                }

                continue;
            }

            var value = SqlTypeMap.FromReader(reader, i, type, nullable || schema.Relations.ContainsKey(name));
            if (!value.IsNull || nullable)
            {
                UsEmitObject.WriteField(UsValue.FromReference(instance), name, value, 0, 0);
            }
        }

        return instance;
    }

    private static void ClearField(UsEmitObject row, string fieldName) =>
        UsEmitObject.ClearAssignment(UsValue.FromReference(row), fieldName);

    private UsTableSchema Require(string tableName)
    {
        lock (_sync)
        {
            if (!_tables.TryGetValue(tableName, out var schema))
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{tableName}' tablosu yok.", 0, 0);
            }

            return schema;
        }
    }

    private SqlCommand CreateCommand(SqlQueryBuilder.BuiltCommand built)
    {
        EnsureConnection();
        var command = _connection!.CreateCommand();
        command.Transaction = _transaction;
        command.CommandText = built.Text;
        command.CommandTimeout = 30;
        foreach (var parameter in built.Parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private void EnsureConnection()
    {
        if (_connection is null)
        {
            _connection = new SqlConnection(_connectionString);
            _connection.Open();
        }
        else if (_connection.State != System.Data.ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    private void DisposeSession()
    {
        _transaction?.Dispose();
        _transaction = null;
        _connection?.Dispose();
        _connection = null;
    }

    private static UsRuntimeException Translate(SqlException exception, int start, int length)
    {
        var message = "Veritabanı hatası.";
        return new UsRuntimeException(UsRuntimeCodes.WriteRejected, message, start, length);
    }

    public void Dispose() => Reset();

    public ValueTask DisposeAsync()
    {
        Reset();
        return ValueTask.CompletedTask;
    }
}
