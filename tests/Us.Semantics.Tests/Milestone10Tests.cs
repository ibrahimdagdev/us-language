using Us.Core;

namespace Us.Semantics.Tests;

public class TestDeclBindTests
{
    [Fact]
    public void TwoTestsAreDistinctSymbolsInSourceOrder()
    {
        var bound = Bound.File("""
            test "birinci" { }
            test "ikinci" { }
            """);
        Assert.False(bound.Diagnostics.HasErrors);
        Assert.Equal(2, bound.Tests.Length);
        Assert.Equal("birinci", bound.Tests[0].Name);
        Assert.Equal("ikinci", bound.Tests[1].Name);
        Assert.NotEqual(bound.Tests[0].Span, bound.Tests[1].Span);
    }

    [Fact]
    public void NonLiteralNameIsUs0856()
    {
        Bound.One("test 1 { }", DiagnosticCodes.TestName);
    }

    [Fact]
    public void DuplicateNameIsUs0804()
    {
        Bound.One("""
            test "aynı" { }
            test "aynı" { }
            """, DiagnosticCodes.DuplicateName);
    }

    [Fact]
    public void AwaitInsideTestIsMarkedAsyncAndAtestIsNotATest()
    {
        var bound = Bound.File("""
            afonksiyon getir(): tamsayı
            {
                bekle 0.milisaniye
                => 1
            }
            test "bekler"
            {
                sonuç: tamsayı = bekle getir()
            }
            """);
        Assert.False(bound.Diagnostics.HasErrors);
        Assert.True(Assert.Single(bound.Tests).IsAsync);
        var plain = Bound.File("atest \"x\" { }");
        Assert.Empty(plain.Tests);
    }
}

public class ExpectedBindTests
{
    [Fact]
    public void NonBooleanExpectedIsUs0857()
    {
        Bound.One("""
            test "sayı"
            {
                beklenen 1
            }
            """, DiagnosticCodes.ExpectedType);
    }
}
