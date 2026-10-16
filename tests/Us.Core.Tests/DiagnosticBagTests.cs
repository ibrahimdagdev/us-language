namespace Us.Core.Tests;

public class DiagnosticBagTests
{
    [Fact]
    public void PreservesOrderAndPublishesAStableCopy()
    {
        var bag = new DiagnosticBag();
        var first = Sample(DiagnosticSeverity.Warning, "US0001");
        var second = Sample(DiagnosticSeverity.Error, "US1021");
        bag.Add(first);
        var published = bag.ToImmutableArray();
        bag.Add(second);

        Assert.Equal(first, Assert.Single(published));
        Assert.Equal(first, bag.ToImmutableArray()[0]);
        Assert.Equal(second, bag.ToImmutableArray()[1]);
    }

    [Fact]
    public void WarningDoesNotCountAsError()
    {
        var bag = new DiagnosticBag();
        bag.Add(Sample(DiagnosticSeverity.Warning, "US0001"));
        Assert.False(bag.HasErrors);
        bag.Add(Sample(DiagnosticSeverity.Error, "US1021"));
        Assert.True(bag.HasErrors);
    }

    private static Diagnostic Sample(DiagnosticSeverity severity, string code) => new(
        code,
        severity,
        "örnek",
        "ana.us",
        new TextSpan(0, 1),
        1,
        1,
        null);
}
