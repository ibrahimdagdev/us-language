using Us.Data.SqlServer;
using Us.Runtime;

namespace Us.Runtime.Tests;

public class SqlServerUnitTests
{
    public SqlServerUnitTests()
    {
        SqlServerRegistration.Register();
        UsEmitDatabase.UseBackend(null);
        UsEmitDatabase.Reset();
    }

    [Fact]
    public void IdentifierQuoteEscapesBrackets()
    {
        Assert.Equal("[Users]", SqlIdentifier.Quote("Users"));
        Assert.Equal("[Weird]]Name]", SqlIdentifier.Quote("Weird]Name"));
    }

    [Fact]
    public void FilterBuildsParameterizedWhereNotConcatenatedValues()
    {
        var schema = UsersSchema();
        var plan = new UsQueryPlan(
            "kullanıcılar",
            [UsQueryExpr.Compare(UsQueryExpr.Field("ad"), "==", UsQueryExpr.Constant(UsValue.FromText("O'Brien'; DROP TABLE t;--")))],
            [],
            []);
        var built = SqlQueryBuilder.Select(schema, plan);
        Assert.Contains("WHERE", built.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("O'Brien", built.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", built.Text, StringComparison.Ordinal);
        Assert.Contains("@p", built.Text, StringComparison.Ordinal);
        Assert.Equal("O'Brien'; DROP TABLE t;--", built.Parameters[0].Value);
    }

    [Fact]
    public void InsertBuildsParameterizedValues()
    {
        var schema = UsersSchema();
        var row = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(row, "id", UsValue.FromInt(1), 0, 0);
        UsEmitObject.WriteField(row, "ad", UsValue.FromText("Ali' OR 1=1"), 0, 0);
        UsEmitObject.WriteField(row, "aktif", UsValue.FromBool(true), 0, 0);
        var built = SqlQueryBuilder.Insert(schema, (UsEmitObject)row.Reference!, new Dictionary<string, int>());
        Assert.DoesNotContain("Ali'", built.Text, StringComparison.Ordinal);
        Assert.Contains("@p", built.Text, StringComparison.Ordinal);
        Assert.Contains(built.Parameters, item => Equals(item.Value, "Ali' OR 1=1"));
    }

    [Fact]
    public void UpdateAndDeleteRequireKeyParameter()
    {
        var schema = UsersSchema();
        var row = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(row, "id", UsValue.FromInt(7), 0, 0);
        UsEmitObject.WriteField(row, "ad", UsValue.FromText("x'; DROP TABLE y;--"), 0, 0);
        UsEmitObject.WriteField(row, "aktif", UsValue.FromBool(false), 0, 0);
        var update = SqlQueryBuilder.Update(schema, (UsEmitObject)row.Reference!, new Dictionary<string, int>());
        Assert.Contains("WHERE", update.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP TABLE", update.Text, StringComparison.Ordinal);
        var delete = SqlQueryBuilder.Delete(schema, (UsEmitObject)row.Reference!);
        Assert.Contains("DELETE FROM", delete.Text, StringComparison.Ordinal);
        Assert.Equal(7, delete.Parameters[0].Value);
    }

    [Fact]
    public void NullComparisonUsesIsNull()
    {
        var schema = UsersSchema();
        var plan = new UsQueryPlan(
            "kullanıcılar",
            [UsQueryExpr.Compare(UsQueryExpr.Field("ad"), "==", UsQueryExpr.Constant(UsValue.Null))],
            [],
            []);
        var built = SqlQueryBuilder.Select(schema, plan);
        Assert.Contains("IS NULL", built.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("= @", built.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeMapCoversCoreScalars()
    {
        Assert.Contains("nvarchar", SqlTypeMap.SqlType("metin", false), StringComparison.Ordinal);
        Assert.Contains("int", SqlTypeMap.SqlType("tamsayı", false), StringComparison.Ordinal);
        Assert.Contains("bit", SqlTypeMap.SqlType("mantıksal", false), StringComparison.Ordinal);
        Assert.Contains("decimal", SqlTypeMap.SqlType("hondalık", true), StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredFilterExprWorksOnMemoryProvider()
    {
        UsEmitDatabase.RegisterTable(
            "kullanıcılar",
            "Kullanıcı",
            ["id", "ad", "aktif"],
            ["tamsayı", "metin", "mantıksal"],
            [false, false, false],
            [false, true, true],
            "id");
        var a = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(a, "ad", UsValue.FromText("Ali' OR 1=1"), 0, 0);
        UsEmitObject.WriteField(a, "aktif", UsValue.FromBool(true), 0, 0);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), a, 0, 0));

        var query = UsEmitDatabase.FilterExpr(
            UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
            UsQueryExpr.Compare(UsQueryExpr.Field("ad"), "==", UsQueryExpr.Constant(UsValue.FromText("Ali' OR 1=1"))),
            0,
            0);
        var list = Await(UsEmitDatabase.All(query, 0, 0));
        Assert.Single(((UsEmitList)list.Reference!).Items);
    }

    [Fact]
    public void Integration_CrudWhenConnectionConfigured()
    {
        var connection = Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            return; // skipped — no live SQL Server
        }

        using (var wipe = new Microsoft.Data.SqlClient.SqlConnection(connection))
        {
            wipe.Open();
            using var command = wipe.CreateCommand();
            command.CommandText = "IF OBJECT_ID(N'[kullanıcılar]', N'U') IS NOT NULL DROP TABLE [kullanıcılar];";
            command.ExecuteNonQuery();
        }

        SqlServerRegistration.Register();
        using var backend = new SqlServerBackend(connection);
        UsEmitDatabase.UseBackend(backend);
        UsEmitDatabase.Reset();
        var schema = UsersSchema();
        backend.RegisterTable(schema);
        SqlSchemaSync.Apply(connection, [schema], approve: true);

        UsEmitDatabase.RegisterTable(
            schema.Name,
            schema.ModelName,
            schema.FieldNames,
            schema.FieldTypes,
            schema.Nullable,
            schema.Required,
            schema.KeyField);

        var row = UsEmitObject.Create("Kullanıcı", schema.FieldNames, schema.Nullable);
        UsEmitObject.WriteField(row, "ad", UsValue.FromText("O'Brien"), 0, 0);
        UsEmitObject.WriteField(row, "aktif", UsValue.FromBool(true), 0, 0);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), row, 0, 0));

        var all = Await(UsEmitDatabase.All(
            UsEmitDatabase.FilterExpr(
                UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
                UsQueryExpr.Compare(UsQueryExpr.Field("ad"), "==", UsQueryExpr.Constant(UsValue.FromText("O'Brien"))),
                0,
                0),
            0,
            0));
        Assert.Single(((UsEmitList)all.Reference!).Items);
        UsEmitDatabase.UseBackend(null);
    }

    private static UsTableSchema UsersSchema() => new()
    {
        Name = "kullanıcılar",
        ModelName = "Kullanıcı",
        FieldNames = ["id", "ad", "aktif"],
        FieldTypes = ["tamsayı", "metin", "mantıksal"],
        Nullable = [false, false, false],
        Required = [false, true, true],
        KeyField = "id",
    };

    private static UsValue Await(UsValue value)
    {
        var operation = Assert.IsType<UsOperation>(value.Reference);
        return operation.Task.GetAwaiter().GetResult();
    }
}
