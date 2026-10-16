using Us.Runtime;

namespace Us.Interpreter.Tests;

public class JsonDiagnosticTests
{
    [Fact]
    public void FieldNullIntoNonNullableIsUs1541()
    {
        var result = Run.Text("""
            model Kutu { ad: metin }
            k: Kutu = json.oku<Kutu>("{{ \"ad\": null }}")
            """);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.JsonNull);
        Assert.DoesNotContain(result.Diagnostics, item => item.Code == UsRuntimeCodes.NullStore);
    }

    [Fact]
    public void CycleOnWriteIsUs1529()
    {
        var result = Run.Text("""
            model Düğüm { sonraki: Düğüm? }
            a: Düğüm = @Düğüm { sonraki = ~ }
            a.sonraki = a
            syaz json.yaz(a)
            """);
        Assert.Contains(result.Diagnostics, item => item.Code == UsRuntimeCodes.JsonCycle);
    }
}
