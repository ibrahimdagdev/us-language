using Us.Core;
using Us.Parser;
using Us.Semantics;

namespace Us.Semantics.Tests;

public static class Bound
{
    public static BoundCompilation File(string text)
    {
        var source = SourceFile.FromText("deneme.us", text.Replace("\r\n", "\n", StringComparison.Ordinal));
        return Binder.Bind(source);
    }

    public static void Clean(string text)
    {
        var bound = File(text);
        Assert.False(bound.Diagnostics.HasErrors, string.Join(Environment.NewLine, bound.Diagnostics.ToImmutableArray().Select(item => $"{item.Code}:{item.Line}:{item.Column} {item.Message}")));
    }

    public static Diagnostic One(string text, string code)
    {
        var bound = File(text);
        return Assert.Single(bound.Diagnostics.ToImmutableArray(), item => item.Code == code);
    }
}

public class ScopeTests
{
    [Fact]
    public void InnerScopeSeesOuterSymbol()
    {
        var outer = new Scope(null);
        var inner = new Scope(outer);
        var type = new PrimitiveTypeSymbol("metin");
        Assert.True(outer.TryDeclare(type));
        Assert.Equal(type, Assert.Single(inner.Lookup("metin")));
    }

    [Fact]
    public void SameScopeRejectsTheSecondVariable()
    {
        var scope = new Scope(null);
        Assert.True(scope.TryDeclare(new VariableSymbol("ad", new PrimitiveTypeSymbol("metin"))));
        Assert.False(scope.TryDeclare(new VariableSymbol("ad", new PrimitiveTypeSymbol("metin"))));
    }
}

public class BinderSkeletonTests
{
    [Fact]
    public void EmptyFileBinds()
    {
        var bound = Bound.File("");
        Assert.Empty(bound.Members);
        Assert.False(bound.Diagnostics.HasErrors);
    }

    [Fact]
    public void UnknownNodeProducesOneDiagnostic()
    {
        var file = SourceFile.FromText("deneme.us", " ");
        var token = new SyntaxToken(new Lexer.Token(Lexer.TokenKind.Identifier, new TextSpan(0, 0), default), true);
        var constraint = new ConstraintSyntax(token, null, null);
        var end = new SyntaxToken(new Lexer.Token(Lexer.TokenKind.EndOfFile, new TextSpan(1, 0), default), false);
        var unit = new CompilationUnitSyntax([constraint], end);
        var bound = Binder.Bind(file, unit);
        var diagnostic = Assert.Single(bound.Diagnostics.ToImmutableArray());
        Assert.Equal(DiagnosticCodes.UnknownSyntax, diagnostic.Code);
    }
}

public class PrimitiveTypeTests
{
    [Fact]
    public void FourteenPrimitivesResolve()
    {
        foreach (var name in new[] { "metin", "karakter", "tamsayı", "utamsayı", "ondalık", "gondalık", "hondalık", "mantıksal", "nesne", "dinamik", "tarih", "saat", "tarihsaat", "süre" })
        {
            Bound.Clean($"değer: {name}");
        }
    }

    [Fact]
    public void NullableTextIsNullableText()
    {
        Bound.Clean("telefon: metin? = ~");
    }
}

public class UnresolvedNameTests
{
    [Fact]
    public void MissingNameIsUs0801OnTheName()
    {
        var diagnostic = Bound.One("syaz olmayan", DiagnosticCodes.UnresolvedName);
        Assert.Equal(5, diagnostic.Span.Start);
        Assert.Equal(7, diagnostic.Span.Length);
    }
}

public class LocalBindingTests
{
    [Fact]
    public void TypeMismatchSuggestsTheNumber()
    {
        var diagnostic = Bound.One("yaş: tamsayı = \"38\"", DiagnosticCodes.InitializerMismatch);
        Assert.Equal("38", diagnostic.Suggestion);
        Assert.Contains("tamsayı", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("metin", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InitializerCountMustMatch()
    {
        Bound.One("a, b: tamsayı = 1", DiagnosticCodes.InitializerCount);
    }
}

public class ShadowingTests
{
    [Fact]
    public void InnerNameCannotShadow()
    {
        Bound.One("ad: metin = \"Ali\"\neğer doğru\n{\n    ad: metin = \"Veli\"\n}", DiagnosticCodes.Shadowing);
    }

    [Fact]
    public void SameBlockCannotRepeat()
    {
        Bound.One("ad: metin = \"Ali\"\nad: metin = \"Veli\"", DiagnosticCodes.DuplicateName);
    }
}

public class LiteralBindTests
{
    [Fact]
    public void LiteralsHaveTheirTypes()
    {
        Bound.Clean("a: tamsayı = 38\nb: ondalık = 1.5\nc: metin = \"x\"\nd: karakter = 'a'\ne: mantıksal = doğru");
    }

    [Fact]
    public void BareNullNeedsContext()
    {
        Bound.One("syaz ~", DiagnosticCodes.NullWithoutContext);
    }
}

public class OperatorBindTests
{
    [Fact]
    public void BooleanCannotBeAdded()
    {
        Bound.One("doğru + yanlış", DiagnosticCodes.BinaryType);
    }

    [Fact]
    public void BangRequiresBoolean()
    {
        Bound.One("!\"metin\"", DiagnosticCodes.UnaryOrAssignment);
    }
}

public class ConversionTests
{
    [Fact]
    public void IntegerWidensToFloating()
    {
        Bound.Clean("a: ondalık = 1\nb: gondalık = 1\nc: hondalık = 1");
    }

    [Fact]
    public void UnsignedDoesNotImplicitlyBecomeSigned()
    {
        Bound.One("fonksiyon u(): utamsayı => u()\na: utamsayı = u()\nb: tamsayı = 0\nb = a", DiagnosticCodes.IllegalConversion);
    }
}

public class NullableBindTests
{
    [Fact]
    public void NonNullableRejectsNull()
    {
        Bound.One("ad: metin = ~", DiagnosticCodes.NullNotAllowed);
    }

    [Fact]
    public void DirectMemberOnNullableIsDiagnosed()
    {
        Bound.One("ad: metin? = ~\nn: tamsayı = ad.uzunluk", DiagnosticCodes.NullableAccess);
    }

    [Fact]
    public void ConditionalAccessAndCoalesceClearNullable()
    {
        Bound.Clean("ad: metin? = ~\nn: tamsayı = ad?.uzunluk ?? 0");
    }
}

public class IfBindTests
{
    [Fact]
    public void ConditionMustBeBoolean()
    {
        Bound.One("eğer 10\n{\n}", DiagnosticCodes.ConditionType);
    }

    [Fact]
    public void ExpressionIfArmsNeedACommonType()
    {
        Bound.One("değer: nesne = eğer doğru => \"a\" !=> 1", DiagnosticCodes.ExpressionIfArms);
    }
}

public class LoopBindTests
{
    [Fact]
    public void BreakOutsideLoopIsDiagnosed()
    {
        Bound.One("çık", DiagnosticCodes.BadJump);
    }

    [Fact]
    public void NonPositiveStepIsDiagnosed()
    {
        Bound.One("dön 0 ile 10 adım 0 -> i\n{\n}", DiagnosticCodes.LoopStep);
    }
}

public class DefiniteAssignmentTests
{
    [Fact]
    public void ReadBeforeAssignIsDiagnosed()
    {
        Bound.One("yaş: tamsayı\nn: tamsayı = yaş", DiagnosticCodes.DefiniteAssignment);
    }

    [Fact]
    public void BothBranchesAssign()
    {
        Bound.Clean("değer: tamsayı\neğer doğru\n{\n    değer = 1\n}\ndeğilse\n{\n    değer = 2\n}\na: ondalık = değer");
    }
}

public class FunctionBindTests
{
    [Fact]
    public void CallChecksArgumentCount()
    {
        Bound.One("fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı => a + b\nsonuç: tamsayı = topla(1)", DiagnosticCodes.BadCall);
    }

    [Fact]
    public void ValidCallResolves()
    {
        Bound.Clean("fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı => a + b\nsonuç: tamsayı = topla(1, 2)");
    }

    [Fact]
    public void MissingReturnPathIsDiagnosed()
    {
        Bound.One("fonksiyon durum(x: tamsayı): metin\n{\n    eğer x > 0\n    {\n        => \"Pozitif\"\n    }\n}", DiagnosticCodes.MissingReturn);
    }

    [Fact]
    public void VoidFunctionCannotReturnAValue()
    {
        Bound.One("fonksiyon bildir()\n{\n    => 10\n}", DiagnosticCodes.UnexpectedResult);
    }

    [Fact]
    public void ForwardCallResolves()
    {
        Bound.Clean("fonksiyon a(): tamsayı => b()\nfonksiyon b(): tamsayı => 1");
    }

    [Fact]
    public void DefaultParameterIsChecked()
    {
        Bound.Clean("fonksiyon selamla(ad: metin, mesaj: metin = \"Merhaba\")\n{\n    syaz mesaj\n}\nselamla(\"Ali\")");
    }

    [Fact]
    public void RequiredParameterCannotFollowOptional()
    {
        Bound.One("fonksiyon f(a: tamsayı = 1, b: tamsayı)\n{\n}", DiagnosticCodes.OptionalOrder);
    }
}

public class OverloadTests
{
    [Fact]
    public void EqualCandidatesAreAmbiguous()
    {
        Bound.One("fonksiyon f(a: ondalık): tamsayı => 1\nfonksiyon f(a: hondalık): tamsayı => 2\nx: tamsayı = 1\nsonuç: tamsayı = f(x)", DiagnosticCodes.AmbiguousCall);
    }
}

public class ModelBindTests
{
    [Fact]
    public void UnknownInitializerIsDiagnosed()
    {
        Bound.One("model Kullanıcı\n{\n    ad: metin\n}\ndeğer: Kullanıcı = @Kullanıcı { olmayan = 1 }", DiagnosticCodes.UnknownMember);
    }

    [Fact]
    public void UnknownConstraintIsDiagnosed()
    {
        Bound.One("model Kullanıcı\n{\n    ad: metin : bilinmeyen\n}", DiagnosticCodes.UnknownConstraint);
    }
}

public class ClassBindTests
{
    [Fact]
    public void PrivateMethodIsHidden()
    {
        Bound.One("sınıf Kutu\n{\n    fonksiyon gizli(): tamsayı => 1\n}\nkutu: Kutu = @Kutu()\nsonuç: tamsayı = kutu.gizli()", DiagnosticCodes.NotVisible);
    }

    [Fact]
    public void ThisIsInvalidAtTopLevel()
    {
        Bound.One("syaz bu", DiagnosticCodes.ThisContext);
    }

    [Fact]
    public void TwoClassBasesAreRejected()
    {
        Bound.One("sınıf A\n{\n}\nsınıf B\n{\n}\nsınıf C : A, B\n{\n}", DiagnosticCodes.MultipleClassBases);
    }

    [Fact]
    public void MissingInterfaceMemberIsDiagnosed()
    {
        Bound.One("arayüz Uçabilir\n{\n    fonksiyon uç()\n}\nsınıf Kuş : Uçabilir\n{\n}", DiagnosticCodes.MissingImplementation);
    }

    [Fact]
    public void BadOverrideIsDiagnosed()
    {
        Bound.One("sınıf A\n{\n    sanal fonksiyon ses()\n    {\n    }\n}\nsınıf B : A\n{\n    yerinegeç fonksiyon yok()\n    {\n    }\n}", DiagnosticCodes.BadOverride);
    }

    [Fact]
    public void AbstractTypeCannotBeCreated()
    {
        Bound.One("soyut sınıf Şekil\n{\n}\ndeğer: Şekil = @Şekil()", DiagnosticCodes.AbstractCreation);
    }

    [Fact]
    public void SealedTypeCannotBeDerived()
    {
        Bound.One("mühürlü sınıf A\n{\n}\nsınıf B : A\n{\n}", DiagnosticCodes.SealedBase);
    }
}

public class GenericBindTests
{
    [Fact]
    public void CallInfersTypeArgument()
    {
        Bound.Clean("fonksiyon ilk<T>(öğe: T): T => öğe\nsonuç: tamsayı = ilk(4)");
    }

    [Fact]
    public void ConstraintFailureIsDiagnosed()
    {
        var bound = Bound.File("arayüz Varlık\n{\n}\nsınıf Depo<T: Varlık>\n{\n}\ndeğer: Depo<tamsayı> = @Depo<tamsayı>()");
        Assert.Contains(bound.Diagnostics.ToImmutableArray(), item => item.Code == DiagnosticCodes.ConstraintFailed);
        Assert.All(bound.Diagnostics.ToImmutableArray(), item => Assert.Equal(DiagnosticCodes.ConstraintFailed, item.Code));
    }
}

public class OptionBindTests
{
    [Fact]
    public void DuplicateOptionMemberIsDiagnosed()
    {
        Bound.One("seçenek Durum\n{\n    Açık\n    Açık\n}", DiagnosticCodes.DuplicateOption);
    }
}

public class QueryBindTests
{
    [Fact]
    public void CurrentItemOutsideQueryIsDiagnosed()
    {
        Bound.One("syaz ^.id", DiagnosticCodes.CurrentItemContext);
    }

    [Fact]
    public void FilterPredicateMustBeBoolean()
    {
        Bound.One("model Kullanıcı\n{\n    ad: metin\n    aktif: mantıksal\n}\nkullanıcılar: liste<Kullanıcı> = []\nkullanıcılar.filtrele(^.ad)", DiagnosticCodes.PredicateType);
    }

    [Fact]
    public void CurrentItemResolvesInsideFilter()
    {
        Bound.Clean("model Kullanıcı\n{\n    aktif: mantıksal\n}\nkullanıcılar: liste<Kullanıcı> = []\nsonuç: liste<Kullanıcı> = kullanıcılar.filtrele(^.aktif)");
    }
}

public class AsyncBindTests
{
    [Fact]
    public void AwaitInsideFunctionIsDiagnosed()
    {
        Bound.One("fonksiyon x()\n{\n    bekle syaz \"a\"\n}", DiagnosticCodes.AwaitContext);
    }

    [Fact]
    public void TopLevelAwaitIsValid()
    {
        Bound.Clean("sonuç: metin = bekle metinGetir()\nfonksiyon metinGetir(): metin => \"a\"");
    }

    [Fact]
    public void RollbackOutsideTransactionIsDiagnosed()
    {
        Bound.One("gerial", DiagnosticCodes.TransactionContext);
    }

    [Fact]
    public void TransactionCanCommit()
    {
        Bound.Clean("işlem\n{\n    onayla\n}");
    }
}

public class ExtraSemanticTests
{
    [Fact]
    public void ReturnExpressionMustMatch()
    {
        Bound.One("fonksiyon kare(x: tamsayı): tamsayı => \"yanlış\"", DiagnosticCodes.TypeMismatch);
    }

    [Fact]
    public void RefRejectsLiteral()
    {
        Bound.One("fonksiyon artır(ref sayı: tamsayı)\n{\n    sayı = sayı + 1\n}\nartır(ref 10)", DiagnosticCodes.RefTarget);
    }

    [Fact]
    public void ConcreteFunctionNeedsBody()
    {
        Bound.One("fonksiyon bildir()", DiagnosticCodes.MissingBody);
    }

    [Fact]
    public void DurationMemberIsDuration()
    {
        Bound.Clean("bekleme: süre = 30.dakika");
    }

    [Fact]
    public void InheritanceCycleIsDiagnosed()
    {
        Bound.One("sınıf A : B\n{\n}\nsınıf B : A\n{\n}", DiagnosticCodes.InheritanceCycle);
    }

    [Fact]
    public void ModelCopyResolvesMembers()
    {
        Bound.Clean("model Kullanıcı\n{\n    ad: metin\n}\nkaynak: Kullanıcı = @Kullanıcı { ad = \"Ali\" }\nkopya: Kullanıcı = kaynak ile { ad = \"Ahmet\" }");
    }

    [Fact]
    public void MappingResolvesCurrentItem()
    {
        Bound.Clean("model Kullanıcı\n{\n    id: tamsayı\n}\nmodel KullanıcıDto\n{\n    id: tamsayı\n}\ndönüşüm Kullanıcı => KullanıcıDto\n{\n    id = ^.id\n}");
    }
}

