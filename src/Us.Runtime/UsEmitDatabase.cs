namespace Us.Runtime;

/// <summary>
/// In-memory tables and transactions for emitted IL. Mirrors interpreter
/// <c>MemoryDatabase</c> transaction API without referencing Us.Semantics.
/// Provider-neutral query chain: filter / sort / include / materialize.
/// </summary>
public static class UsEmitDatabase
{
    private const int TxNone = 0;
    private const int TxActive = 1;
    private const int TxClosed = 2;

    private sealed class RelationMeta
    {
        public required string RelatedTableName { get; init; }

        public required string RelatedKeyField { get; init; }
    }

    private sealed class StoredRow
    {
        public required UsEmitObject Values { get; init; }

        public Dictionary<string, int> Relations { get; } = new(StringComparer.Ordinal);

        public StoredRow Clone()
        {
            var copy = new StoredRow { Values = Values.Clone() };
            foreach (var pair in Relations)
            {
                copy.Relations[pair.Key] = pair.Value;
            }

            return copy;
        }
    }

    private sealed class TableStore
    {
        public required string Name { get; init; }

        public required string ModelName { get; init; }

        public required string[] FieldNames { get; init; }

        public required string[] FieldTypes { get; init; }

        public required bool[] Nullable { get; init; }

        public required bool[] Required { get; init; }

        public string? KeyField { get; init; }

        public Dictionary<string, RelationMeta> RelationFields { get; } = new(StringComparer.Ordinal);

        public List<StoredRow> Rows { get; } = [];
    }

    private static readonly Dictionary<string, TableStore> Tables = new(StringComparer.Ordinal);
    private static readonly object Sync = new();
    private static Dictionary<string, List<StoredRow>>? _snapshot;
    private static int _tx = TxNone;
    private static IUsDatabaseBackend? _backend;
    private static Func<string, IUsDatabaseBackend>? _backendFactory;
    private static Action<IReadOnlyList<UsTableSchema>, bool>? _schemaSync;

    /// <summary>Optional SQL (or other) backend. Null = built-in memory provider.</summary>
    public static IUsDatabaseBackend? Backend => _backend;

    /// <summary>Host/tests register SQL Server factory without Us.Runtime referencing SqlClient.</summary>
    public static void RegisterBackendFactory(Func<string, IUsDatabaseBackend> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _backendFactory = factory;
    }

    /// <summary>
    /// Activates SQL backend when <c>US_TEST_SQLSERVER_CONNECTION</c> or
    /// <c>US_SQLSERVER_CONNECTION</c> is set; otherwise memory.
    /// </summary>
    public static void ConfigureFromEnvironment()
    {
        var connection = Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION")
            ?? Environment.GetEnvironmentVariable("US_SQLSERVER_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            _backend = null;
            return;
        }

        if (_backendFactory is null)
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.NotExecutable,
                "SQL Server sağlayıcısı kayıtlı değil.",
                0,
                0);
        }

        _backend = _backendFactory(connection);
    }

    public static void UseBackend(IUsDatabaseBackend? backend) => _backend = backend;

    /// <summary>
    /// Snapshot of registered table schemas for SQL additive sync at compiled-app startup.
    /// </summary>
    public static IReadOnlyList<UsTableSchema> ExportSchemas()
    {
        lock (Sync)
        {
            var list = new List<UsTableSchema>(Tables.Count);
            foreach (var store in Tables.Values)
            {
                var schema = new UsTableSchema
                {
                    Name = store.Name,
                    ModelName = store.ModelName,
                    FieldNames = store.FieldNames,
                    FieldTypes = store.FieldTypes,
                    Nullable = store.Nullable,
                    Required = store.Required,
                    KeyField = store.KeyField,
                };
                foreach (var pair in store.RelationFields)
                {
                    schema.Relations[pair.Key] = new UsRelationSchema
                    {
                        FieldName = pair.Key,
                        RelatedTableName = pair.Value.RelatedTableName,
                        RelatedKeyField = pair.Value.RelatedKeyField,
                    };
                }

                list.Add(schema);
            }

            return list;
        }
    }

    /// <summary>
    /// When a SQL backend is active, applies additive schema sync from registered tables.
    /// No-op for memory. Destructive steps require <paramref name="approve"/>.
    /// </summary>
    public static void EnsureSqlSchema(bool approve = true)
    {
        if (_backend is null || _schemaSync is null)
        {
            return;
        }

        _schemaSync(ExportSchemas(), approve);
    }

    /// <summary>SQL provider registers schema sync without Us.Runtime referencing SqlClient.</summary>
    public static void RegisterSchemaSync(Action<IReadOnlyList<UsTableSchema>, bool> sync)
    {
        ArgumentNullException.ThrowIfNull(sync);
        _schemaSync = sync;
    }

    public static void Reset()
    {
        lock (Sync)
        {
            Tables.Clear();
            _snapshot = null;
            _tx = TxNone;
        }

        _backend?.Reset();
    }

    public static void RegisterTable(
        string tableName,
        string modelName,
        string[] fieldNames,
        bool[] nullable,
        bool[] required,
        string? keyField) =>
        RegisterTable(tableName, modelName, fieldNames, fieldNames.Select(_ => "metin").ToArray(), nullable, required, keyField);

    public static void RegisterTable(
        string tableName,
        string modelName,
        string[] fieldNames,
        string[] fieldTypes,
        bool[] nullable,
        bool[] required,
        string? keyField)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(fieldNames);
        ArgumentNullException.ThrowIfNull(fieldTypes);
        ArgumentNullException.ThrowIfNull(nullable);
        ArgumentNullException.ThrowIfNull(required);
        lock (Sync)
        {
            Tables[tableName] = new TableStore
            {
                Name = tableName,
                ModelName = modelName,
                FieldNames = fieldNames,
                FieldTypes = fieldTypes,
                Nullable = nullable,
                Required = required,
                KeyField = keyField,
            };
        }

        _backend?.RegisterTable(new UsTableSchema
        {
            Name = tableName,
            ModelName = modelName,
            FieldNames = fieldNames,
            FieldTypes = fieldTypes,
            Nullable = nullable,
            Required = required,
            KeyField = keyField,
        });
    }

    /// <summary>
    /// Registers a flat FK-style relationship for <c>birlikte</c> include hydration.
    /// </summary>
    public static void RegisterRelation(
        string tableName,
        string fieldName,
        string relatedTableName,
        string relatedKeyField)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(relatedTableName);
        ArgumentNullException.ThrowIfNull(relatedKeyField);
        lock (Sync)
        {
            if (!Tables.TryGetValue(tableName, out var store))
            {
                throw new InvalidOperationException($"'{tableName}' tablosu kayıtlı değil.");
            }

            store.RelationFields[fieldName] = new RelationMeta
            {
                RelatedTableName = relatedTableName,
                RelatedKeyField = relatedKeyField,
            };
        }

        _backend?.RegisterRelation(tableName, fieldName, relatedTableName, relatedKeyField);
    }

    /// <summary>Handle stored in the <c>db</c> local from <c>bağla db</c>.</summary>
    public static UsValue Bind() => UsValue.FromReference(EmitDatabaseHandle.Instance);

    public static UsValue OpenQuery(string tableName, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        lock (Sync)
        {
            if (!Tables.ContainsKey(tableName))
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.UnknownTable,
                    $"'{tableName}' tablosu yok.",
                    start,
                    length);
            }
        }

        return UsValue.FromReference(new UsEmitTableQuery(tableName));
    }

    public static void Begin()
    {
        if (_backend is not null)
        {
            _backend.Begin();
            return;
        }

        lock (Sync)
        {
            if (_tx == TxActive)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem zaten açık.", 0, 0);
            }

            _snapshot = Tables.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Rows.Select(row => row.Clone()).ToList(),
                StringComparer.Ordinal);
            _tx = TxActive;
        }
    }

    public static void Commit()
    {
        if (_backend is not null)
        {
            _backend.Commit();
            return;
        }

        lock (Sync)
        {
            if (_tx != TxActive)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
            }

            _snapshot = null;
            _tx = TxClosed;
        }
    }

    public static void Rollback()
    {
        if (_backend is not null)
        {
            _backend.Rollback();
            return;
        }

        lock (Sync)
        {
            if (_tx != TxActive)
            {
                throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
            }

            if (_snapshot is not null)
            {
                foreach (var pair in _snapshot)
                {
                    var rows = Tables[pair.Key].Rows;
                    rows.Clear();
                    rows.AddRange(pair.Value);
                }
            }

            _snapshot = null;
            _tx = TxClosed;
        }
    }

    /// <summary>Matches interpreter <c>FinishTransaction</c>: no-op when not active.</summary>
    public static void Finish(bool commit)
    {
        if (_backend is not null)
        {
            _backend.Finish(commit);
            return;
        }

        lock (Sync)
        {
            if (_tx != TxActive)
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

    public static UsValue Filter(UsValue query, Func<UsValue, bool> predicate, int start, int length)
    {
        var current = RequireQuery(query, start, length);
        return UsValue.FromReference(new UsEmitTableQuery(current.TableName, current, "filtrele", predicate, null, false, null, null, null));
    }

    /// <summary>Structured filter for SQL-capable providers (no delegate reverse-engineering).</summary>
    public static UsValue FilterExpr(UsValue query, UsQueryExpr expression, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var current = RequireQuery(query, start, length);
        return UsValue.FromReference(new UsEmitTableQuery(current.TableName, current, "filtrele", null, null, false, null, expression, null));
    }

    public static UsValue Sort(UsValue query, Func<UsValue, UsValue> selector, bool descending, int start, int length)
    {
        var current = RequireQuery(query, start, length);
        var name = descending ? "tsırala" : "sırala";
        return UsValue.FromReference(new UsEmitTableQuery(current.TableName, current, name, null, selector, descending, null, null, null));
    }

    /// <summary>Structured sort by bound field name.</summary>
    public static UsValue SortByField(UsValue query, string fieldName, bool descending, int start, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        var current = RequireQuery(query, start, length);
        var name = descending ? "tsırala" : "sırala";
        return UsValue.FromReference(new UsEmitTableQuery(current.TableName, current, name, null, null, descending, null, null, fieldName));
    }

    /// <summary>
    /// Flat relationship include (<c>birlikte</c>). Not a SQL JOIN — hydrates related
    /// models from FK relations captured on insert/update.
    /// </summary>
    public static UsValue Include(UsValue query, string fieldName, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        var current = RequireQuery(query, start, length);
        return UsValue.FromReference(new UsEmitTableQuery(current.TableName, current, "birlikte", null, null, false, fieldName, null, null));
    }

    public static UsValue Insert(UsValue query, UsValue row, int start, int length)
    {
        var tableQuery = RequireQuery(query, start, length);
        var instance = RequireObject(row, start, length);
        if (_backend is not null)
        {
            InsertViaBackend(tableQuery.TableName, row, instance, start, length);
            return Done(UsValue.Null);
        }

        lock (Sync)
        {
            var store = Open(tableQuery.TableName, start, length);
            if (store.KeyField is not null)
            {
                UsEmitObject.WriteField(
                    row,
                    store.KeyField,
                    UsValue.FromInt(NextKey(store, store.KeyField)),
                    start,
                    length);
            }

            EnsureValid(store, instance, start, length);
            store.Rows.Add(Capture(instance, store));
        }

        return Done(UsValue.Null);
    }

    public static UsValue Update(UsValue query, UsValue row, int start, int length)
    {
        var tableQuery = RequireQuery(query, start, length);
        var instance = RequireObject(row, start, length);
        if (_backend is not null)
        {
            UpdateViaBackend(tableQuery.TableName, instance, start, length);
            return Done(UsValue.Null);
        }

        lock (Sync)
        {
            var store = Open(tableQuery.TableName, start, length);
            var index = FindRow(store, instance, start, length);
            EnsureValid(store, instance, start, length);
            store.Rows[index] = Capture(instance, store);
        }

        return Done(UsValue.Null);
    }

    public static UsValue Delete(UsValue query, UsValue row, int start, int length)
    {
        var tableQuery = RequireQuery(query, start, length);
        var instance = RequireObject(row, start, length);
        if (_backend is not null)
        {
            _backend.Delete(tableQuery.TableName, instance, start, length);
            return Done(UsValue.Null);
        }

        lock (Sync)
        {
            var store = Open(tableQuery.TableName, start, length);
            var index = FindRow(store, instance, start, length);
            store.Rows.RemoveAt(index);
        }

        return Done(UsValue.Null);
    }

    public static UsValue All(UsValue query, int start, int length)
    {
        var tableQuery = RequireQuery(query, start, length);
        List<UsEmitObject> rows;
        if (_backend is not null)
        {
            if (!TryBuildPlan(tableQuery, out var plan))
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.NotExecutable,
                    "SQL sağlayıcı yapılandırılmış sorgu ifadesi gerektirir.",
                    start,
                    length);
            }

            rows = _backend.Query(plan, start, length).ToList();
        }
        else
        {
            lock (Sync)
            {
                var store = Open(tableQuery.TableName, start, length);
                rows = Materialize(store, tableQuery, start, length);
            }
        }

        var list = new UsEmitList("liste");
        foreach (var row in rows)
        {
            list.Items.Add(UsValue.FromReference(row));
        }

        return Done(UsValue.FromReference(list));
    }

    private static void InsertViaBackend(string tableName, UsValue row, UsEmitObject instance, int start, int length)
    {
        lock (Sync)
        {
            if (!Tables.TryGetValue(tableName, out var store))
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{tableName}' tablosu yok.", start, length);
            }

            if (store.KeyField is not null)
            {
                UsEmitObject.WriteField(
                    row,
                    store.KeyField,
                    UsValue.FromInt(_backend!.NextKey(tableName, store.KeyField)),
                    start,
                    length);
            }

            EnsureValid(store, instance, start, length);
            var captured = Capture(instance, store);
            _backend!.Insert(tableName, captured.Values, captured.Relations, start, length);
        }
    }

    private static void UpdateViaBackend(string tableName, UsEmitObject instance, int start, int length)
    {
        lock (Sync)
        {
            if (!Tables.TryGetValue(tableName, out var store))
            {
                throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{tableName}' tablosu yok.", start, length);
            }

            EnsureValid(store, instance, start, length);
            var captured = Capture(instance, store);
            _backend!.Update(tableName, captured.Values, captured.Relations, start, length);
        }
    }

    internal static bool TryBuildPlan(UsEmitTableQuery query, out UsQueryPlan plan)
    {
        var chain = Chain(query);
        var filters = new List<UsQueryExpr>();
        var sorts = new List<UsQuerySort>();
        var includes = new List<string>();
        foreach (var step in chain)
        {
            if (step.Operation == "filtrele")
            {
                if (step.FilterExpr is null)
                {
                    plan = null!;
                    return false;
                }

                filters.Add(step.FilterExpr);
            }
            else if (step.Operation is "sırala" or "tsırala")
            {
                if (step.SortField is null)
                {
                    plan = null!;
                    return false;
                }

                sorts.Add(new UsQuerySort(step.SortField, step.Descending));
            }
            else if (step.Operation == "birlikte" && step.IncludeField is not null)
            {
                includes.Add(step.IncludeField);
            }
        }

        plan = new UsQueryPlan(query.TableName, filters, sorts, includes);
        return true;
    }

    private static List<UsEmitTableQuery> Chain(UsEmitTableQuery query)
    {
        var chain = new List<UsEmitTableQuery>();
        for (var current = query; current is not null; current = current.Previous)
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }

    private static List<UsEmitObject> Materialize(TableStore store, UsEmitTableQuery query, int start, int length)
    {
        var chain = Chain(query);
        var rows = store.Rows.Select(row => row.Clone()).ToList();
        Func<UsValue, UsValue>? sort = null;
        string? sortField = null;
        var descending = false;
        var includes = new List<string>();
        foreach (var step in chain)
        {
            if (step.Operation == "filtrele")
            {
                if (step.FilterExpr is not null)
                {
                    rows = rows.Where(row => UsQueryEval.Matches(step.FilterExpr, row.Values)).ToList();
                }
                else if (step.Predicate is not null)
                {
                    rows = rows
                        .Where(row => step.Predicate(UsValue.FromReference(row.Values)))
                        .ToList();
                }
            }
            else if (step.Operation is "sırala" or "tsırala")
            {
                if (step.SortField is not null)
                {
                    sortField = step.SortField;
                    sort = null;
                    descending = step.Descending;
                }
                else if (step.Selector is not null)
                {
                    sort = step.Selector;
                    sortField = null;
                    descending = step.Descending;
                }
            }
            else if (step.Operation == "birlikte" && step.IncludeField is not null)
            {
                includes.Add(step.IncludeField);
            }
        }

        if (sortField is not null)
        {
            var keyed = rows
                .Select(row => (Row: row, Key: UsQueryEval.Evaluate(UsQueryExpr.Field(sortField), row.Values)))
                .ToList();
            rows = (descending
                    ? keyed.OrderByDescending(item => item.Key, UsEmitValueComparer.Instance)
                    : keyed.OrderBy(item => item.Key, UsEmitValueComparer.Instance))
                .Select(item => item.Row)
                .ToList();
        }
        else if (sort is not null)
        {
            var keyed = rows
                .Select(row => (Row: row, Key: sort(UsValue.FromReference(row.Values))))
                .ToList();
            rows = (descending
                    ? keyed.OrderByDescending(item => item.Key, UsEmitValueComparer.Instance)
                    : keyed.OrderBy(item => item.Key, UsEmitValueComparer.Instance))
                .Select(item => item.Row)
                .ToList();
        }

        var result = new List<UsEmitObject>(rows.Count);
        foreach (var row in rows)
        {
            var instance = row.Values.Clone();
            foreach (var include in includes)
            {
                HydrateInclude(instance, row, store, include, start, length);
            }

            result.Add(instance);
        }

        return result;
    }

    private static void HydrateInclude(
        UsEmitObject instance,
        StoredRow row,
        TableStore store,
        string fieldName,
        int start,
        int length)
    {
        if (!row.Relations.TryGetValue(fieldName, out var key))
        {
            return;
        }

        if (!store.RelationFields.TryGetValue(fieldName, out var meta))
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.UnknownTable,
                $"'{fieldName}' için tablo yok.",
                start,
                length);
        }

        if (!Tables.TryGetValue(meta.RelatedTableName, out var related))
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.UnknownTable,
                $"'{fieldName}' için tablo yok.",
                start,
                length);
        }

        var match = related.Rows.FirstOrDefault(item =>
            item.Values.TryGetField(meta.RelatedKeyField, out var value)
            && value.Kind == UsValueKind.Int
            && value.Int == key);
        if (match is null)
        {
            return;
        }

        UsEmitObject.WriteField(
            UsValue.FromReference(instance),
            fieldName,
            UsValue.FromReference(match.Values.Clone()),
            start,
            length);
    }

    private static StoredRow Capture(UsEmitObject instance, TableStore store)
    {
        var values = new UsEmitObject(instance.TypeName, store.FieldNames, store.Nullable);
        var row = new StoredRow { Values = values };
        foreach (var name in store.FieldNames)
        {
            if (!instance.IsAssigned(name) || !instance.TryGetField(name, out var value))
            {
                continue;
            }

            if (store.RelationFields.TryGetValue(name, out var meta))
            {
                if (value.Reference is UsEmitObject related
                    && related.IsAssigned(meta.RelatedKeyField)
                    && related.TryGetField(meta.RelatedKeyField, out var stored)
                    && stored.Kind == UsValueKind.Int)
                {
                    row.Relations[name] = stored.Int;
                }

                continue;
            }

            UsEmitObject.WriteField(UsValue.FromReference(values), name, value, 0, 0);
        }

        return row;
    }

    private static TableStore Open(string tableName, int start, int length)
    {
        if (!Tables.TryGetValue(tableName, out var store))
        {
            throw new UsRuntimeException(
                UsRuntimeCodes.UnknownTable,
                $"'{tableName}' tablosu yok.",
                start,
                length);
        }

        return store;
    }

    private static void EnsureValid(TableStore store, UsEmitObject instance, int start, int length)
    {
        for (var index = 0; index < store.FieldNames.Length; index++)
        {
            var name = store.FieldNames[index];
            if (string.Equals(name, store.KeyField, StringComparison.Ordinal))
            {
                continue;
            }

            if (store.RelationFields.ContainsKey(name))
            {
                continue;
            }

            instance.TryGetField(name, out var value);
            var assigned = instance.IsAssigned(name);
            var effective = assigned ? value : UsValue.Null;
            if (index < store.Required.Length && store.Required[index] && IsEmpty(effective))
            {
                throw new UsRuntimeException(
                    UsRuntimeCodes.WriteRejected,
                    $"'{name}' zorunludur.",
                    start,
                    length);
            }
        }
    }

    private static int FindRow(TableStore store, UsEmitObject instance, int start, int length)
    {
        if (store.KeyField is null)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", start, length);
        }

        if (!instance.IsAssigned(store.KeyField))
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", start, length);
        }

        var key = UsEmitObject.ReadField(UsValue.FromReference(instance), store.KeyField, start, length);
        var index = store.Rows.FindIndex(row =>
            row.Values.TryGetField(store.KeyField, out var stored) && Same(stored, key));
        if (index < 0)
        {
            throw new UsRuntimeException(UsRuntimeCodes.MissingRecord, "Kayıt bulunamadı.", start, length);
        }

        return index;
    }

    private static int NextKey(TableStore store, string keyField)
    {
        var max = 0;
        foreach (var row in store.Rows)
        {
            if (row.Values.TryGetField(keyField, out var value) && value.Kind == UsValueKind.Int && value.Int > max)
            {
                max = value.Int;
            }
        }

        return max + 1;
    }

    private static UsEmitTableQuery RequireQuery(UsValue query, int start, int length) =>
        query.Reference as UsEmitTableQuery
        ?? throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, "Tablo sorgusu yok.", start, length);

    private static UsEmitObject RequireObject(UsValue row, int start, int length) =>
        row.Reference as UsEmitObject
        ?? throw new UsRuntimeException(UsRuntimeCodes.WriteRejected, "Kayıt model değil.", start, length);

    private static bool IsEmpty(UsValue value) =>
        value.IsNull || value.Kind == UsValueKind.Text && (value.Text ?? string.Empty).Length == 0;

    private static bool Same(UsValue left, UsValue right) =>
        left.Kind == right.Kind && left.Kind switch
        {
            UsValueKind.Null => true,
            UsValueKind.Bool => left.Bool == right.Bool,
            UsValueKind.Int => left.Int == right.Int,
            UsValueKind.UInt => left.UInt == right.UInt,
            UsValueKind.Text => string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            _ => left.Equals(right),
        };

    private static UsValue Done(UsValue value) =>
        UsValue.FromReference(new UsOperation(Task.FromResult(value)));
}

public sealed class EmitDatabaseHandle
{
    public static readonly EmitDatabaseHandle Instance = new();

    private EmitDatabaseHandle()
    {
    }
}

public sealed class UsEmitTableQuery
{
    public UsEmitTableQuery(string tableName)
    {
        TableName = tableName;
    }

    public UsEmitTableQuery(
        string tableName,
        UsEmitTableQuery previous,
        string operation,
        Func<UsValue, bool>? predicate,
        Func<UsValue, UsValue>? selector,
        bool descending,
        string? includeField,
        UsQueryExpr? filterExpr,
        string? sortField)
    {
        TableName = tableName;
        Previous = previous;
        Operation = operation;
        Predicate = predicate;
        Selector = selector;
        Descending = descending;
        IncludeField = includeField;
        FilterExpr = filterExpr;
        SortField = sortField;
    }

    public string TableName { get; }

    public UsEmitTableQuery? Previous { get; }

    public string? Operation { get; }

    public Func<UsValue, bool>? Predicate { get; }

    public Func<UsValue, UsValue>? Selector { get; }

    public bool Descending { get; }

    public string? IncludeField { get; }

    public UsQueryExpr? FilterExpr { get; }

    public string? SortField { get; }
}

file sealed class UsEmitValueComparer : IComparer<UsValue>
{
    public static readonly UsEmitValueComparer Instance = new();

    public int Compare(UsValue left, UsValue right)
    {
        if (left.IsNull && right.IsNull)
        {
            return 0;
        }

        if (left.IsNull)
        {
            return -1;
        }

        if (right.IsNull)
        {
            return 1;
        }

        if (left.Kind == UsValueKind.Text && right.Kind == UsValueKind.Text)
        {
            return string.CompareOrdinal(left.Text, right.Text);
        }

        if (left.Kind == UsValueKind.Bool && right.Kind == UsValueKind.Bool)
        {
            return left.Bool.CompareTo(right.Bool);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return ToDecimal(left).CompareTo(ToDecimal(right));
        }

        return string.CompareOrdinal(left.ToDisplay(), right.ToDisplay());
    }

    private static bool IsNumber(UsValue value) =>
        value.Kind is UsValueKind.Int or UsValueKind.UInt or UsValueKind.Float64 or UsValueKind.Float32 or UsValueKind.Decimal;

    private static decimal ToDecimal(UsValue value) => value.Kind switch
    {
        UsValueKind.Int => value.Int,
        UsValueKind.UInt => value.UInt,
        UsValueKind.Decimal => value.Decimal,
        UsValueKind.Float32 => (decimal)value.Float32,
        UsValueKind.Float64 => (decimal)value.Float64,
        _ => 0,
    };
}
