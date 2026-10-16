using Us.Runtime;

namespace Us.Runtime.Tests;

public class UsValueTests
{
    [Fact]
    public void IntegerIsNotTheSameValueAsItsText()
    {
        Assert.NotEqual(UsValue.FromInt(38), UsValue.FromText("38"));
    }

    [Fact]
    public void NullIsItsOwnKind()
    {
        Assert.Equal(UsValueKind.Null, UsValue.Null.Kind);
        Assert.NotEqual(UsValue.Unit, UsValue.Null);
    }

    [Fact]
    public void NullCannotBeStoredInANonNullableCell()
    {
        var exception = Assert.Throws<UsRuntimeException>(() => UsValue.Null.EnsureCanStore(false, 0, 1));
        Assert.Equal(UsRuntimeCodes.NullStore, exception.Code);
    }

    [Fact]
    public void IntegerWidensToTheDocumentedClrTypes()
    {
        Assert.Equal(38d, UsMath.Convert(UsValue.FromInt(38), UsValueKind.Float64, 0, 0).Float64);
        Assert.Equal(38f, UsMath.Convert(UsValue.FromInt(38), UsValueKind.Float32, 0, 0).Float32);
        Assert.Equal(38m, UsMath.Convert(UsValue.FromInt(38), UsValueKind.Decimal, 0, 0).Decimal);
        Assert.Equal(38d, UsMath.Convert(UsValue.FromUInt(38), UsValueKind.Float64, 0, 0).Float64);
    }

    [Fact]
    public void RealLiteralDoesNotUseTheHostDecimalSeparator()
    {
        var value = double.Parse("1.5", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(1.5, value);
        Assert.Equal("1.5", value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DivisionByZeroIsUs1502()
    {
        var exception = Assert.Throws<UsRuntimeException>(() => UsMath.Divide(UsValue.FromInt(10), UsValue.FromInt(0), UsValueKind.Int, 3, 1));
        Assert.Equal(UsRuntimeCodes.DivisionByZero, exception.Code);
        Assert.Contains("Sıfıra", exception.Message, StringComparison.Ordinal);
    }
}
