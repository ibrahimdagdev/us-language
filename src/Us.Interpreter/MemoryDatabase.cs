using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter;

public interface IDatabaseProvider
{
    TableStore Open(TableSymbol table);
}

public enum TransactionState
{
    None,
    Active,
    Committed,
    RolledBack,
}

public sealed class TableStore
{
    internal TableStore(TableSymbol table)
    {
        Table = table;
        Model = table.Model;
    }

    public TableSymbol Table { get; }

    public TypeSymbol Model { get; }

    internal List<MemoryRow> Rows { get; } = [];

    public int Count => Rows.Count;
}

public sealed class MemoryRow
{
    public Dictionary<FieldSymbol, UsValue> Values { get; } = [];

    public Dictionary<FieldSymbol, int> Relations { get; } = [];

    public MemoryRow Clone()
    {
        var copy = new MemoryRow();
        foreach (var pair in Values)
        {
            copy.Values[pair.Key] = pair.Value;
        }

        foreach (var pair in Relations)
        {
            copy.Relations[pair.Key] = pair.Value;
        }

        return copy;
    }
}

public sealed class UsTableQuery
{
    public UsTableQuery(MemoryDatabase database, TableSymbol table)
    {
        Database = database;
        Table = table;
    }

    private UsTableQuery(UsTableQuery previous, string operation, BoundNode? argument, FieldSymbol? include, bool descending)
    {
        Database = previous.Database;
        Table = previous.Table;
        Previous = previous;
        Operation = operation;
        Argument = argument;
        Include = include;
        Descending = descending;
    }

    public MemoryDatabase Database { get; }

    public TableSymbol Table { get; }

    public UsTableQuery? Previous { get; }

    public string? Operation { get; }

    public BoundNode? Argument { get; }

    public FieldSymbol? Include { get; }

    public bool Descending { get; }

    public UsTableQuery With(string operation, BoundNode? argument, FieldSymbol? include, bool descending) =>
        new(this, operation, argument, include, descending);
}

public sealed class MemoryDatabase : IDatabaseProvider
{
    private readonly Dictionary<TableSymbol, TableStore> _stores = [];
    private Dictionary<TableSymbol, List<MemoryRow>>? _snapshot;
    private TransactionState _state = TransactionState.None;

    public MemoryDatabase(IEnumerable<DatabaseSymbol> databases)
    {
        foreach (var database in databases)
        {
            foreach (var table in database.Tables)
            {
                _stores[table] = new TableStore(table);
            }
        }
    }

    public TransactionState State => _state;

    public int OpenTransactions => _state == TransactionState.Active ? 1 : 0;

    public string LastPlan { get; set; } = string.Empty;

    internal IEnumerable<TableStore> OpenTables() => _stores.Values;

    public TableStore Open(TableSymbol table)
    {
        if (!_stores.TryGetValue(table, out var store))
        {
            throw new UsRuntimeException(UsRuntimeCodes.UnknownTable, $"'{table.Name}' tablosu yok.", 0, 0);
        }

        return store;
    }

    public void Begin()
    {
        if (_state == TransactionState.Active)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem zaten açık.", 0, 0);
        }

        _snapshot = _stores.ToDictionary(pair => pair.Key, pair => pair.Value.Rows.Select(row => row.Clone()).ToList());
        _state = TransactionState.Active;
    }

    public void Commit()
    {
        if (_state != TransactionState.Active)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
        }

        _snapshot = null;
        _state = TransactionState.Committed;
    }

    public void Rollback()
    {
        if (_state != TransactionState.Active)
        {
            throw new UsRuntimeException(UsRuntimeCodes.ClosedTransaction, "İşlem kapanmış.", 0, 0);
        }

        if (_snapshot is not null)
        {
            foreach (var pair in _snapshot)
            {
                var rows = _stores[pair.Key].Rows;
                rows.Clear();
                rows.AddRange(pair.Value);
            }
        }

        _snapshot = null;
        _state = TransactionState.RolledBack;
    }
}
