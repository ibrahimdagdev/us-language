namespace Us.Compiler.Tests;

public class TopLevelAwaitParityTests
{
    [Fact]
    public void TopLevelAwaitMatchesSampleShape()
    {
        PhaseAParity.AssertParity("""
            afonksiyon bekleVeYaz()
            {
                bekle 15.milisaniye
                syaz "Tamam"
            }
            bekle bekleVeYaz()
            """);
    }

    [Fact]
    public void TopLevelAwaitOfValueMatches()
    {
        PhaseAParity.AssertParity("""
            afonksiyon sayı(): tamsayı
            {
                bekle 5.milisaniye
                => 7
            }
            değer: tamsayı = bekle sayı()
            syaz "{değer}"
            """);
    }
}
