using Us.Core;

namespace Us.Semantics.Tests;

public class RefOutBinderTests
{
    [Fact]
    public void RefRequiresInitializedVariable()
    {
        Bound.One("""
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı
            artır(ref değer)
            """, DiagnosticCodes.DefiniteAssignment);
    }

    [Fact]
    public void MissingRefModifierIsBadCall()
    {
        Bound.One("""
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 1
            artır(değer)
            """, DiagnosticCodes.BadCall);
    }

    [Fact]
    public void WrongOutModifierIsBadCall()
    {
        Bound.One("""
            fonksiyon artır(ref sayı: tamsayı)
            {
                sayı++
            }
            değer: tamsayı = 1
            artır(çıktı değer)
            """, DiagnosticCodes.BadCall);
    }

    [Fact]
    public void CiktiMustBeAssignedBeforeReturn()
    {
        Bound.One("""
            fonksiyon üret(çıktı değer: tamsayı)
            {
            }
            """, DiagnosticCodes.OutputAssignment);
    }

    [Fact]
    public void ExpressionBodiedCiktiMustAssign()
    {
        Bound.One("""
            fonksiyon üret(çıktı değer: tamsayı): tamsayı => 1
            """, DiagnosticCodes.OutputAssignment);
    }

    [Fact]
    public void CiktiMayBeUnassignedAtCallSite()
    {
        Bound.Clean("""
            fonksiyon yazDeğer(çıktı değer: tamsayı)
            {
                değer = 1
            }
            hedef: tamsayı
            yazDeğer(çıktı hedef)
            """);
    }

    [Fact]
    public void FieldRefTargetBinds()
    {
        Bound.Clean("""
            model Kutu
            {
                sayı: tamsayı
            }
            fonksiyon artır(ref x: tamsayı)
            {
                x++
            }
            k: Kutu = @Kutu { sayı = 1 }
            artır(ref k.sayı)
            """);
    }

    [Fact]
    public void IndexedRefTargetRejected()
    {
        Bound.One("""
            fonksiyon artır(ref x: tamsayı)
            {
                x++
            }
            dizi: liste<tamsayı> = [1, 2]
            artır(ref dizi[0])
            """, DiagnosticCodes.RefTarget);
    }
}
