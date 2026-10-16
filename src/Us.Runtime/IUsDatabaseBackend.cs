namespace Us.Runtime;

/// <summary>
/// Provider-neutral database engine used by <see cref="UsEmitDatabase"/>.
/// Memory is built-in; SQL Server registers via <see cref="UsEmitDatabase.RegisterBackendFactory"/>.
/// </summary>
public interface IUsDatabaseBackend
{
    void Reset();

    void RegisterTable(UsTableSchema schema);

    void RegisterRelation(string tableName, string fieldName, string relatedTableName, string relatedKeyField);

    void Begin();

    void Commit();

    void Rollback();

    void Finish(bool commit);

    int NextKey(string tableName, string keyField);

    void Insert(string tableName, UsEmitObject row, IReadOnlyDictionary<string, int> relations, int start, int length);

    void Update(string tableName, UsEmitObject row, IReadOnlyDictionary<string, int> relations, int start, int length);

    void Delete(string tableName, UsEmitObject row, int start, int length);

    /// <summary>Execute a fully structured query plan and return materialized rows (includes applied).</summary>
    IReadOnlyList<UsEmitObject> Query(UsQueryPlan plan, int start, int length);
}
