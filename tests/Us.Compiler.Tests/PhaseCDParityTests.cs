namespace Us.Compiler.Tests;

public class TypeLayoutTests
{
    [Fact]
    public void EmptyModelCompilesAndConstructs() => PhaseAParity.AssertParity("""
        model Boş
        {
        }
        x: Boş = @Boş { }
        syaz "ok"
        """);
}

public class ModelFieldParityTests
{
    [Fact]
    public void ModelFieldsMatch() => PhaseAParity.AssertParity("""
        model Kullanıcı
        {
            ad: metin
            yaş: tamsayı
        }
        kullanıcı: Kullanıcı = @Kullanıcı
        {
            ad = "İbrahim"
            yaş = 38
        }
        syaz kullanıcı.ad
        syaz "{kullanıcı.yaş}"
        """);
}

public class ArrayParityTests
{
    [Fact]
    public void ListLiteralAndCountMatch() => PhaseAParity.AssertParity("""
        isimler: liste<metin> = ["Ali", "Ayşe"]
        syaz "{isimler.adet}"
        """);
}

public class QueryParityTests
{
    [Fact]
    public void FilterAndFirstMatch() => PhaseAParity.AssertParity("""
        değerler: liste<tamsayı> = [1, 2, 3, 4]
        büyük: liste<tamsayı> = değerler.filtrele(^ > 2)
        syaz "{büyük.adet}"
        syaz "{büyük.ilk}"
        """);
}

public class PhaseCGateTests
{
    [Fact]
    public void ModelConstructionParity() => PhaseAParity.AssertParity("""
        model Nokta
        {
            x: tamsayı
            y: tamsayı
        }
        p: Nokta = @Nokta { x = 1, y = 2 }
        syaz "{p.x + p.y}"
        """);
}

public class PhaseDGateTests
{
    [Fact]
    public void CollectionQueryParity() => PhaseAParity.AssertParity("""
        sayılar: liste<tamsayı> = [10, 20, 30]
        syaz "{sayılar.adet}"
        syaz "{sayılar.filtrele(^ >= 20).adet}"
        """);
}
