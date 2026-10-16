namespace Us.Compiler.Tests;

public class EventParityTests
{
    [Fact]
    public void HandlersRunInOrderAndInstancesStaySeparate()
    {
        PhaseAParity.AssertParity("""
            sınıf Kutu
            {
                genel olay değişti: metin
                genel n: tamsayı
                genel fonksiyon bir(değer: metin)
                {
                    syaz "1"
                    n = n + 1
                }
                genel fonksiyon iki(değer: metin)
                {
                    syaz "2"
                }
                genel fonksiyon bağla()
                {
                    değişti += bir
                    değişti += iki
                }
            }
            kutu: Kutu = @Kutu()
            diğer: Kutu = @Kutu()
            kutu.bağla()
            kutu.değişti.tetikle("a")
            diğer.değişti.tetikle("b")
            syaz "{kutu.n}"
            kutu.değişti -= kutu.bir
            kutu.değişti.tetikle("c")
            """);
    }

    [Fact]
    public void HandlerErrorsContinueAndVirtualDispatchMatches()
    {
        PhaseAParity.AssertParity("""
            sınıf Kutu
            {
                genel olay değişti: metin
                genel fonksiyon pat(değer: metin)
                {
                    fırlat @Hata(değer)
                }
                genel fonksiyon iki(değer: metin)
                {
                    syaz "2"
                }
            }
            sınıf Taban
            {
                genel olay oldu: metin
                genel sanal fonksiyon ses(değer: metin)
                {
                    syaz "taban"
                }
            }
            sınıf Türev : Taban
            {
                genel yerinegeç fonksiyon ses(değer: metin)
                {
                    syaz "türev"
                }
            }
            kutu: Kutu = @Kutu()
            kutu.değişti += kutu.pat
            kutu.değişti += kutu.iki
            dene
            {
                kutu.değişti.tetikle("x")
            }
            yakala hata
            {
                syaz hata.mesaj
            }
            türev: Taban = @Türev()
            türev.oldu += türev.ses
            türev.oldu.tetikle("z")
            """);
    }
}

public class PhaseFGateTests
{
    [Fact]
    public void AsyncConcurrentAndEventsParity()
    {
        PhaseAParity.AssertParity("""
            afonksiyon gecik(): tamsayı
            {
                bekle 5.milisaniye
                => 1
            }
            sınıf Sayaç
            {
                genel olay tik: metin
                genel fonksiyon bildir(değer: metin)
                {
                    syaz değer
                }
            }
            a: tamsayı = 0
            b: tamsayı = 0
            eşişlem
            {
                a = bekle gecik()
                b = bekle gecik()
            }
            s: Sayaç = @Sayaç()
            s.tik += s.bildir
            s.tik.tetikle("ok")
            syaz "{a + b}"
            """);
    }
}
