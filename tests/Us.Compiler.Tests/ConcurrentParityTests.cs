namespace Us.Compiler.Tests;

public class ConcurrentParityTests
{
    [Fact]
    public void IndependentAssignmentsMatch()
    {
        PhaseAParity.AssertParity("""
            afonksiyon bir(): tamsayı
            {
                bekle 10.milisaniye
                => 4
            }
            a: tamsayı = 0
            b: tamsayı = 0
            eşişlem
            {
                a = bekle bir()
                b = bekle bir()
            }
            syaz "{a + b}"
            """);
    }

    [Fact]
    public void AggregateErrorsPreserveSourceOrder()
    {
        PhaseAParity.AssertParity("""
            afonksiyon kötü(mesaj: metin): tamsayı
            {
                fırlat @Hata(mesaj)
                => 0
            }
            değer: tamsayı = 1
            dene
            {
                eşişlem
                {
                    değer = bekle kötü("bir")
                    bekle kötü("iki")
                }
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            sonunda
            {
                syaz "son"
            }
            syaz "{değer}"
            """);
    }
}
