using System.Text.Json;
using Us.Runtime;

namespace Us.Runtime.Tests;

public class UsEmitJsonTests
{
    [Fact]
    public void FieldNullIntoNonNullableIsUs1541NotUs1500()
    {
        var exception = Assert.Throws<UsRuntimeException>(() =>
            UsEmitJson.Read(
                UsValue.FromText("""{"ad":null}"""),
                "Kutu",
                ["ad"],
                ["metin"],
                [false],
                [true],
                [""],
                0,
                0));
        Assert.Equal(UsRuntimeCodes.JsonNull, exception.Code);
    }

    [Fact]
    public void FieldNullIntoNullableSucceeds()
    {
        var value = UsEmitJson.Read(
            UsValue.FromText("""{"ad":null}"""),
            "Kutu",
            ["ad"],
            ["metin"],
            [true],
            [false],
            [""],
            0,
            0);
        Assert.False(value.IsNull);
        var field = UsEmitObject.ReadField(value, "ad", 0, 0);
        Assert.True(field.IsNull);
    }

    [Fact]
    public void NestedShapeRoundTrip()
    {
        var adresShape = UsEmitJson.EncodeShape(
            "Adres",
            ["şehir"],
            ["metin"],
            [false],
            [true],
            [""]);
        var kişiShape = UsEmitJson.EncodeShape(
            "Kişi",
            ["ad", "adres"],
            ["metin", adresShape],
            [false, false],
            [true, true],
            ["", ""]);

        var written = UsEmitJson.Write(
            UsEmitJson.Read(
                UsValue.FromText("""{"ad":"İbrahim","adres":{"şehir":"İstanbul"}}"""),
                "Kişi",
                ["ad", "adres"],
                ["metin", adresShape],
                [false, false],
                [true, true],
                ["", ""],
                0,
                0),
            0,
            0);

        using var document = JsonDocument.Parse(written.Text!);
        Assert.Equal("İbrahim", document.RootElement.GetProperty("ad").GetString());
        Assert.Equal("İstanbul", document.RootElement.GetProperty("adres").GetProperty("şehir").GetString());
        _ = kişiShape;
    }

    [Fact]
    public void ListeOfModelRoundTrip()
    {
        var adresShape = UsEmitJson.EncodeShape(
            "Adres",
            ["şehir"],
            ["metin"],
            [false],
            [true],
            [""]);
        var kişiShape = UsEmitJson.EncodeShape(
            "Kişi",
            ["ad", "adres"],
            ["metin", adresShape],
            [false, false],
            [true, true],
            ["", ""]);
        var list = UsEmitJson.Read(
            UsValue.FromText("""[{"ad":"Ali","adres":{"şehir":"Ankara"}}]"""),
            "liste",
            [],
            [kişiShape],
            [],
            [],
            [],
            0,
            0);
        var emitList = Assert.IsType<UsEmitList>(list.Reference);
        Assert.Single(emitList.Items);
        Assert.Equal("Ali", UsEmitObject.ReadField(emitList.Items[0], "ad", 0, 0).Text);
    }

    [Fact]
    public void CycleOnWriteIsUs1529()
    {
        var obj = new UsEmitObject("Düğüm", ["sonraki"], [true]);
        var boxed = UsValue.FromReference(obj);
        UsEmitObject.WriteField(boxed, "sonraki", boxed, 0, 0);
        var exception = Assert.Throws<UsRuntimeException>(() => UsEmitJson.Write(boxed, 0, 0));
        Assert.Equal(UsRuntimeCodes.JsonCycle, exception.Code);
    }

    [Fact]
    public void NullableMarkerOnTypeDescAllowsNull()
    {
        var value = UsEmitJson.Read(UsValue.FromText("null"), "metin?", [], [], [], [], [], 0, 0);
        Assert.True(value.IsNull);
    }

    [Fact]
    public void MissingNonNullableFieldIsUs1528()
    {
        var exception = Assert.Throws<UsRuntimeException>(() =>
            UsEmitJson.Read(
                UsValue.FromText("{}"),
                "Kutu",
                ["ad"],
                ["metin"],
                [false],
                [true],
                [""],
                0,
                0));
        Assert.Equal(UsRuntimeCodes.MissingJsonField, exception.Code);
    }

    [Fact]
    public void UnrelatedNullStoreRemainsUs1500()
    {
        var boxed = UsValue.FromReference(new UsEmitObject("Kutu", ["ad"], [false]));
        var exception = Assert.Throws<UsRuntimeException>(() =>
            UsEmitObject.WriteField(boxed, "ad", UsValue.Null, 0, 0));
        Assert.Equal(UsRuntimeCodes.NullStore, exception.Code);
    }
}
