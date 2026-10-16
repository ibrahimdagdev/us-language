using Us.Runtime;

namespace Us.Runtime.Tests;

public class UsEmitDatabaseTests
{
    public UsEmitDatabaseTests()
    {
        UsEmitDatabase.Reset();
    }

    [Fact]
    public void FlatIncludeHydratesRelatedModel()
    {
        RegisterOrdersSchema();
        var user = NewUser("Ada");
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), user, 0, 0));

        var order = NewOrder(user);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), order, 0, 0));

        var list = Await(UsEmitDatabase.All(
            UsEmitDatabase.Include(UsEmitDatabase.OpenQuery("siparişler", 0, 0), "kullanıcı", 0, 0),
            0,
            0));
        var items = ((UsEmitList)list.Reference!).Items;
        Assert.Single(items);
        var related = UsEmitObject.ReadField(items[0], "kullanıcı", 0, 0);
        Assert.Equal("Ada", UsEmitObject.ReadField(related, "ad", 0, 0).Text);
    }

    [Fact]
    public void IncludeWithNoRelationLeavesFieldUnassigned()
    {
        RegisterOrdersSchema();
        var order = UsEmitObject.Create("Sipariş", ["id", "kullanıcı"], [false, false]);
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), order, 0, 0));

        var list = Await(UsEmitDatabase.All(
            UsEmitDatabase.Include(UsEmitDatabase.OpenQuery("siparişler", 0, 0), "kullanıcı", 0, 0),
            0,
            0));
        var items = ((UsEmitList)list.Reference!).Items;
        Assert.Single(items);
        Assert.False(((UsEmitObject)items[0].Reference!).IsAssigned("kullanıcı"));
    }

    [Fact]
    public void IncludeAcrossParentsKeepsChildrenIsolated()
    {
        RegisterOrdersSchema();
        var ada = NewUser("Ada");
        var ali = NewUser("Ali");
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), ada, 0, 0));
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), ali, 0, 0));
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), NewOrder(ada), 0, 0));
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), NewOrder(ali), 0, 0));

        var list = Await(UsEmitDatabase.All(
            UsEmitDatabase.Include(UsEmitDatabase.OpenQuery("siparişler", 0, 0), "kullanıcı", 0, 0),
            0,
            0));
        var items = ((UsEmitList)list.Reference!).Items;
        Assert.Equal(2, items.Count);
        var first = UsEmitObject.ReadField(items[0], "kullanıcı", 0, 0);
        var second = UsEmitObject.ReadField(items[1], "kullanıcı", 0, 0);
        Assert.Equal("Ada", UsEmitObject.ReadField(first, "ad", 0, 0).Text);
        Assert.Equal("Ali", UsEmitObject.ReadField(second, "ad", 0, 0).Text);
        Assert.False(ReferenceEquals(first.Reference, second.Reference));
    }

    [Fact]
    public void MaterializeWithoutIncludeDoesNotEmbedRelatedObject()
    {
        RegisterOrdersSchema();
        var user = NewUser("Ada");
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0), user, 0, 0));
        Await(UsEmitDatabase.Insert(UsEmitDatabase.OpenQuery("siparişler", 0, 0), NewOrder(user), 0, 0));

        var list = Await(UsEmitDatabase.All(UsEmitDatabase.OpenQuery("siparişler", 0, 0), 0, 0));
        var items = ((UsEmitList)list.Reference!).Items;
        Assert.Single(items);
        Assert.False(((UsEmitObject)items[0].Reference!).IsAssigned("kullanıcı"));
    }

    [Fact]
    public void FilterAndSortComposeBeforeMaterialize()
    {
        UsEmitDatabase.RegisterTable(
            "kullanıcılar",
            "Kullanıcı",
            ["id", "ad", "aktif"],
            [false, false, false],
            [false, true, true],
            "id");

        Await(UsEmitDatabase.Insert(
            UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
            NewActiveUser("Şeyma", true),
            0,
            0));
        Await(UsEmitDatabase.Insert(
            UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
            NewActiveUser("Ali", true),
            0,
            0));
        Await(UsEmitDatabase.Insert(
            UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
            NewActiveUser("Zeynep", false),
            0,
            0));

        var query = UsEmitDatabase.Sort(
            UsEmitDatabase.Filter(
                UsEmitDatabase.OpenQuery("kullanıcılar", 0, 0),
                row => UsEmitObject.ReadField(row, "aktif", 0, 0).Bool,
                0,
                0),
            row => UsEmitObject.ReadField(row, "ad", 0, 0),
            descending: false,
            0,
            0);
        var list = Await(UsEmitDatabase.All(query, 0, 0));
        var names = ((UsEmitList)list.Reference!).Items
            .Select(item => UsEmitObject.ReadField(item, "ad", 0, 0).Text!)
            .ToArray();
        Assert.Equal(new[] { "Ali", "Şeyma" }, names);
    }

    [Fact]
    public void UnknownTableIsUs1547()
    {
        var exception = Assert.Throws<UsRuntimeException>(() => UsEmitDatabase.OpenQuery("yok", 0, 0));
        Assert.Equal(UsRuntimeCodes.UnknownTable, exception.Code);
    }

    private static void RegisterOrdersSchema()
    {
        UsEmitDatabase.RegisterTable(
            "kullanıcılar",
            "Kullanıcı",
            ["id", "ad"],
            [false, false],
            [false, true],
            "id");
        UsEmitDatabase.RegisterTable(
            "siparişler",
            "Sipariş",
            ["id", "kullanıcı"],
            [false, false],
            [false, false],
            "id");
        UsEmitDatabase.RegisterRelation("siparişler", "kullanıcı", "kullanıcılar", "id");
    }

    private static UsValue NewUser(string name)
    {
        var user = UsEmitObject.Create("Kullanıcı", ["id", "ad"], [false, false]);
        UsEmitObject.WriteField(user, "ad", UsValue.FromText(name), 0, 0);
        return user;
    }

    private static UsValue NewActiveUser(string name, bool active)
    {
        var user = UsEmitObject.Create("Kullanıcı", ["id", "ad", "aktif"], [false, false, false]);
        UsEmitObject.WriteField(user, "ad", UsValue.FromText(name), 0, 0);
        UsEmitObject.WriteField(user, "aktif", UsValue.FromBool(active), 0, 0);
        return user;
    }

    private static UsValue NewOrder(UsValue user)
    {
        var order = UsEmitObject.Create("Sipariş", ["id", "kullanıcı"], [false, false]);
        UsEmitObject.WriteField(order, "kullanıcı", user, 0, 0);
        return order;
    }

    private static UsValue Await(UsValue value)
    {
        var operation = Assert.IsType<UsOperation>(value.Reference);
        return operation.Task.GetAwaiter().GetResult();
    }
}
