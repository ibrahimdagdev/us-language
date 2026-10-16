namespace Us.Compiler.Tests;

public class MethodEmitTests
{
    [Fact]
    public void EmptyAndSyazFunctionEmit() => PhaseAParity.AssertParity("""
        fonksiyon selam()
        {
            syaz "Merhaba"
        }
        selam()
        """);
}

public class ReturnParityTests
{
    [Fact]
    public void ParametersAndReturnMatch() => PhaseAParity.AssertParity("""
        fonksiyon çift(n: tamsayı): tamsayı
        {
            => n * 2
        }
        syaz "{çift(21)}"
        """);
}

public class CallRecursionParityTests
{
    [Fact]
    public void RecursionMatches() => PhaseAParity.AssertParity("""
        fonksiyon faktoriyel(n: tamsayı): tamsayı
        {
            eğer n <= 1
            {
                => 1
            }
            => n * faktoriyel(n - 1)
        }
        syaz "{faktoriyel(5)}"
        """);
}

public class DefaultArgParityTests
{
    [Fact]
    public void DefaultArgumentsMatch() => PhaseAParity.AssertParity("""
        fonksiyon selam(ad: metin = "US"): metin
        {
            => "Merhaba {ad}"
        }
        syaz selam()
        syaz selam("İbrahim")
        """);
}

public class PhaseBGateTests
{
    [Fact]
    public void PhaseBRecursionAndCallParity()
    {
        PhaseAParity.AssertParity("""
            fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
            {
                => a + b
            }
            fonksiyon faktoriyel(n: tamsayı): tamsayı
            {
                eğer n <= 1
                {
                    => 1
                }
                => n * faktoriyel(n - 1)
            }
            syaz "{topla(2, 3)}"
            syaz "{faktoriyel(4)}"
            """);
    }
}
