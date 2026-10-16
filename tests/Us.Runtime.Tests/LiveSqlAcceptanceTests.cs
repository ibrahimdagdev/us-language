using Microsoft.Data.SqlClient;
using Us.Data.SqlServer;
using Us.Runtime;

namespace Us.Runtime.Tests;

[CollectionDefinition("LiveSqlAcceptance", DisableParallelization = true)]
public class LiveSqlAcceptanceCollection;

/// <summary>
/// Live SQL Server acceptance — executes only when US_TEST_SQLSERVER_CONNECTION is set.
/// </summary>
[Collection("LiveSqlAcceptance")]
public class LiveSqlAcceptanceTests
{
    private static string? Connection =>
        Environment.GetEnvironmentVariable("US_TEST_SQLSERVER_CONNECTION");

    public LiveSqlAcceptanceTests()
    {
        SqlServerRegistration.Register();
        UsEmitDatabase.UseBackend(null);
        UsEmitDatabase.Reset();
    }

    [Fact]
    public void Live_InjectionAndRoundTrip_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(Connection))
        {
            return;
        }

        using var backend = Open(UsersSchema());
        Insert("O'Brien", aktif: true);
        Insert("Robert'); DROP TABLE kullanıcılar;--", aktif: true);

        var all = Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0));
        var rows = ((UsEmitList)all.Reference!).Items;
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "O'Brien");
        Assert.Contains(rows, row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "Robert'); DROP TABLE kullanıcılar;--");

        var filtered = Await(UsEmitDatabase.All(
            UsEmitDatabase.FilterExpr(
                UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
                UsQueryExpr.Compare(
                    UsQueryExpr.Field("ad"),
                    "==",
                    UsQueryExpr.Constant(UsValue.FromText("O'Brien"))),
                0,
                0),
            0,
            0));
        Assert.Single(((UsEmitList)filtered.Reference!).Items);
        Assert.IsType<SqlServerBackend>(backend);
    }

    [Fact]
    public void Live_TransactionsCommitRollbackAndError_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(Connection))
        {
            return;
        }

        using var backend = Open(UsersSchema());

        UsEmitDatabase.Begin();
        Insert("CommitMe", aktif: true);
        UsEmitDatabase.Commit();
        Assert.Equal(1, CountAll());

        UsEmitDatabase.Begin();
        Insert("RollbackMe", aktif: true);
        UsEmitDatabase.Rollback();
        Assert.Equal(1, CountAll());
        Assert.DoesNotContain(
            ((UsEmitList)Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0)).Reference!).Items,
            row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "RollbackMe");

        try
        {
            UsEmitDatabase.Begin();
            Insert("ErrorMe", aktif: true);
            throw new InvalidOperationException("force-fail");
        }
        catch (InvalidOperationException)
        {
            UsEmitDatabase.Finish(commit: false);
        }

        Assert.Equal(1, CountAll());
        Assert.DoesNotContain(
            ((UsEmitList)Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0)).Reference!).Items,
            row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "ErrorMe");
        Assert.IsType<SqlServerBackend>(backend);
    }

    [Fact]
    public void Live_FilterAndSortThroughSqlBackend_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(Connection))
        {
            return;
        }

        using var backend = Open(UsersSchema());
        Insert("Zed", aktif: true);
        Insert("Ada", aktif: true);
        Insert("Bob", aktif: false);

        var filtered = Await(UsEmitDatabase.All(
            UsEmitDatabase.FilterExpr(
                UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
                UsQueryExpr.Compare(UsQueryExpr.Field("aktif"), "==", UsQueryExpr.Constant(UsValue.FromBool(true))),
                0,
                0),
            0,
            0));
        var names = ((UsEmitList)filtered.Reference!).Items
            .Select(row => UsEmitObject.ReadField(row, "ad", 0, 0).Text)
            .ToArray();
        Assert.Equal(2, names.Length);
        Assert.DoesNotContain("Bob", names);

        var sorted = Await(UsEmitDatabase.All(
            UsEmitDatabase.SortByField(
                UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
                "ad",
                descending: false,
                0,
                0),
            0,
            0));
        var ordered = ((UsEmitList)sorted.Reference!).Items
            .Select(row => UsEmitObject.ReadField(row, "ad", 0, 0).Text!)
            .ToArray();
        Assert.Equal(new[] { "Ada", "Bob", "Zed" }, ordered);
        Assert.IsType<SqlServerBackend>(UsEmitDatabase.Backend);
        Assert.IsType<SqlServerBackend>(backend);
    }

    [Fact]
    public void Live_FlatBirlikte_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(Connection))
        {
            return;
        }

        var users = UsersSchema();
        var orders = new UsTableSchema
        {
            Name = "siparişler",
            ModelName = "Sipariş",
            FieldNames = ["id", "tutar", "kullanıcı"],
            FieldTypes = ["tamsayı", "tamsayı", "model:Kullanıcı"],
            Nullable = [false, false, true],
            Required = [false, true, false],
            KeyField = "id",
        };
        orders.Relations["kullanıcı"] = new UsRelationSchema
        {
            FieldName = "kullanıcı",
            RelatedTableName = "kullanıcılar",
            RelatedKeyField = "id",
        };

        using var backend = Open(users, orders);
        Insert("Ali", aktif: true);
        Insert("Ayşe", aktif: true);
        var userRows = ((UsEmitList)Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0)).Reference!).Items;
        var ali = Assert.Single(userRows, row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "Ali");
        var ayse = Assert.Single(userRows, row => UsEmitObject.ReadField(row, "ad", 0, 0).Text == "Ayşe");
        var aliId = UsEmitObject.ReadField(ali, "id", 0, 0).Int;
        var ayseId = UsEmitObject.ReadField(ayse, "id", 0, 0).Int;

        InsertOrder(aliId, 10);
        InsertOrder(ayseId, 20);
        InsertOrder(aliId, 30);

        var plan = new UsQueryPlan(
            "siparişler",
            [],
            [],
            ["kullanıcı"]);
        var rows = backend.Query(plan, 0, 0);
        Assert.Equal(3, rows.Count);
        foreach (var row in rows)
        {
            var rowValue = UsValue.FromReference(row);
            var tutar = UsEmitObject.ReadField(rowValue, "tutar", 0, 0).Int;
            var user = UsEmitObject.ReadField(rowValue, "kullanıcı", 0, 0);
            Assert.False(user.IsNull);
            var ad = UsEmitObject.ReadField(user, "ad", 0, 0).Text;
            if (tutar is 10 or 30)
            {
                Assert.Equal("Ali", ad);
            }
            else
            {
                Assert.Equal("Ayşe", ad);
            }
        }
    }

    private static SqlServerBackend Open(params UsTableSchema[] schemas)
    {
        var connection = Connection!;
        // Isolate from other live SQL tests sharing UsV020Acceptance.
        using (var wipe = new SqlConnection(connection))
        {
            wipe.Open();
            using var command = wipe.CreateCommand();
            command.CommandText = """
                IF OBJECT_ID(N'[siparişler]', N'U') IS NOT NULL DROP TABLE [siparişler];
                IF OBJECT_ID(N'[kullanıcılar]', N'U') IS NOT NULL DROP TABLE [kullanıcılar];
                """;
            command.ExecuteNonQuery();
        }

        SqlServerRegistration.Register();
        var backend = new SqlServerBackend(connection);
        UsEmitDatabase.UseBackend(backend);
        UsEmitDatabase.Reset();
        foreach (var schema in schemas)
        {
            backend.RegisterTable(schema);
            UsEmitDatabase.RegisterTable(
                schema.Name,
                schema.ModelName,
                schema.FieldNames,
                schema.FieldTypes,
                schema.Nullable,
                schema.Required,
                schema.KeyField);
            foreach (var relation in schema.Relations)
            {
                UsEmitDatabase.RegisterRelation(
                    schema.Name,
                    relation.Key,
                    relation.Value.RelatedTableName,
                    relation.Value.RelatedKeyField);
            }
        }

        var apply = SqlSchemaSync.Apply(connection, schemas, approve: true);
        Assert.DoesNotContain(apply.Diagnostics, item => item.Severity == Us.Core.DiagnosticSeverity.Error);
        return backend;
    }

    private static void Insert(string ad, bool aktif)
    {
        var row = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(row, "ad", UsValue.FromText(ad), 0, 0);
        UsEmitObject.WriteField(row, "aktif", UsValue.FromBool(aktif), 0, 0);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), row, 0, 0));
    }

    private static void InsertOrder(int userId, int tutar)
    {
        var row = UsEmitObject.Create("Sipariş", ["id", "tutar", "kullanıcı"], [false, false, true]);
        UsEmitObject.WriteField(row, "tutar", UsValue.FromInt(tutar), 0, 0);
        var user = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(user, "id", UsValue.FromInt(userId), 0, 0);
        UsEmitObject.WriteField(row, "kullanıcı", user, 0, 0);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), row, 0, 0));
    }

    private static int CountAll()
    {
        var all = Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), 0, 0));
        return ((UsEmitList)all.Reference!).Items.Count;
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
