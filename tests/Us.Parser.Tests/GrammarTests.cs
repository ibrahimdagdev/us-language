using Us.Core;
using Us.Lexer;
using Us.Parser;

namespace Us.Parser.Tests;

public class GrammarTests
{
    [Fact]
    public void ParsesTopLevelStatementsWithoutMain()
    {
        var result = Parsed.Clean("ad: metin = \"İbrahim\"\nsyaz \"Merhaba {ad}\"");
        Assert.Equal(2, result.Root.Members.Length);
        Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        var call = Assert.IsType<ExpressionStatementSyntax>(result.Root.Members[1]);
        Assert.IsType<InvocationExpressionSyntax>(call.Expression);
        Assert.IsType<InterpolationExpressionSyntax>(Assert.IsType<InvocationExpressionSyntax>(call.Expression).Arguments.Arguments[0].Expression);
    }

    [Fact]
    public void KeepsChainedExpressionAcrossNewlines()
    {
        var result = Parsed.Clean("""
            sonuç: liste<Kullanıcı> = db.kullanıcılar
                .filtrele(^.aktif)
                .sırala(^.ad)
                .tümü()
            """);
        Assert.Single(result.Root.Members);
        var declaration = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        Assert.Equal("liste", declaration.Type.Name.Token.Kind == Lexer.TokenKind.Identifier ? "liste" : declaration.Type.Name.Token.Kind.ToString());
        Assert.NotNull(declaration.Type.Arguments);
        var chain = declaration.Initializers[0];
        Assert.IsType<InvocationExpressionSyntax>(chain);
    }

    [Fact]
    public void ParsesNullableAndNestedGenerics()
    {
        var result = Parsed.Clean("değer: liste<Kullanıcı?> = []\ngrup: Sonuç<liste<Kullanıcı>> = ~");
        var first = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        var inner = first.Type.Arguments!.Arguments[0];
        Assert.NotEmpty(inner.Nullables);
        var second = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[1]);
        Assert.NotNull(second.Type.Arguments!.Arguments[0].Arguments);
    }

    [Fact]
    public void ParsesBulkDeclarationWithoutInitializer()
    {
        var result = Parsed.Clean("ad, soyad, şehir: metin = \"İbrahim\", \"Dağ\", \"İstanbul\"\nyaş, sınıf: tamsayı");
        var bulk = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        Assert.Equal(3, bulk.Names.Length);
        Assert.Equal(3, bulk.Initializers.Length);
        var plain = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[1]);
        Assert.Equal(2, plain.Names.Length);
        Assert.Empty(plain.Initializers);
    }

    [Fact]
    public void SplitsStatementIfFromExpressionIf()
    {
        var result = Parsed.Clean("""
            yaş: tamsayı = 38
            eğer yaş >= 18
            {
                syaz "Yetişkin"
            }
            edeğilse yaş >= 13
            {
                syaz "Genç"
            }
            değilse
            {
                syaz "Çocuk"
            }
            durum: metin = eğer yaş >= 18 => "Yetişkin" !=> "Çocuk"
            """);
        var ifStatement = Assert.IsType<IfStatementSyntax>(result.Root.Members[1]);
        Assert.Single(ifStatement.ElseIfs);
        Assert.NotNull(ifStatement.ElseBody);
        var declaration = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[2]);
        Assert.IsType<ExpressionIfSyntax>(declaration.Initializers[0]);
    }

    [Fact]
    public void ParsesLoops()
    {
        var result = Parsed.Clean("""
            dön kullanıcılar -> kullanıcı
            {
            }
            dön 1 ile 10 -> i
            {
            }
            dön 0 ile 100 adım 10 -> i
            {
            }
            tekrarla sayı < 10
            {
            }
            yap
            {
            }
            tekrarla sayı < 10
            sonsuzdön
            {
                çık
                devam
            }
            """);
        Assert.IsType<ForEachStatementSyntax>(result.Root.Members[0]);
        Assert.IsType<RangeForStatementSyntax>(result.Root.Members[1]);
        var stepped = Assert.IsType<RangeForStatementSyntax>(result.Root.Members[2]);
        Assert.NotNull(stepped.Step);
        Assert.IsType<WhileStatementSyntax>(result.Root.Members[3]);
        Assert.IsType<DoWhileStatementSyntax>(result.Root.Members[4]);
        Assert.IsType<InfiniteLoopStatementSyntax>(result.Root.Members[5]);
    }

    [Fact]
    public void ParsesFunctionsLambdasAndResultArrow()
    {
        var result = Parsed.Clean("""
            fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
            {
                => a + b
            }
            fonksiyon kare(x: tamsayı): tamsayı => x * x
            fonksiyon selamla(ad: metin)
            {
                syaz ad
            }
            afonksiyon getir(id: tamsayı): Kullanıcı?
            {
                => bekle bul(id)
            }
            çarpım: tamsayı = (x: tamsayı) => x * 2
            """);
        var blockFunction = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Members[0]);
        Assert.IsType<ResultStatementSyntax>(Assert.IsType<BlockSyntax>(blockFunction.Body!).Statements[0]);
        var expressionFunction = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Members[1]);
        Assert.IsType<BinaryExpressionSyntax>(expressionFunction.Body);
        Assert.IsType<FunctionDeclarationSyntax>(result.Root.Members[2]);
        Assert.Equal(Lexer.TokenKind.Afonksiyon, Assert.IsType<FunctionDeclarationSyntax>(result.Root.Members[3]).Keyword.TokenKind);
        var lambdaDeclaration = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[4]);
        Assert.IsType<LambdaExpressionSyntax>(lambdaDeclaration.Initializers[0]);
    }

    [Fact]
    public void ParsesLambdaBlockWithoutBacktrackingIntoAddition()
    {
        var result = Parsed.Clean("""
            değer: tamsayı = (a + b) * c
            gövde: tamsayı = (x: tamsayı) =>
            {
                sonuç: tamsayı = x * 2
                => sonuç
            }
            """);
        var parenthesized = Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]);
        Assert.IsType<BinaryExpressionSyntax>(parenthesized.Initializers[0]);
        var lambda = Assert.IsType<LambdaExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[1]).Initializers[0]);
        Assert.IsType<BlockSyntax>(lambda.Body);
    }

    [Fact]
    public void ParsesModelClassInterfaceAndOption()
    {
        var result = Parsed.Clean("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad, soyad: metin
                eposta: metin : zorunlu, uzunluk(100)
            }
            sınıf Köpek : Hayvan, Yürüyebilir
            {
                genel oluştur(ad: metin)
                {
                    bu.ad = ad
                }
            }
            arayüz Uçabilir
            {
                fonksiyon uç()
            }
            seçenek SiparişDurumu
            {
                Bekliyor
                Onaylandı
            }
            """);
        var model = Assert.IsType<ModelDeclarationSyntax>(result.Root.Members[0]);
        Assert.NotEmpty(model.Fields[0].Constraints);
        Assert.Equal(2, model.Fields[1].Names.Length);
        var type = Assert.IsType<ClassDeclarationSyntax>(result.Root.Members[1]);
        Assert.Equal(2, type.Bases.Length);
        Assert.IsType<ConstructorDeclarationSyntax>(type.Members[0]);
        var face = Assert.IsType<InterfaceDeclarationSyntax>(result.Root.Members[2]);
        var method = Assert.IsType<FunctionDeclarationSyntax>(face.Members[0]);
        Assert.Null(method.Body);
        Assert.IsType<OptionDeclarationSyntax>(result.Root.Members[3]);
    }

    [Fact]
    public void WildcardIsOnlyTheExactUnderscoreInSelect()
    {
        var result = Parsed.Clean("""
            seç durum
            {
                SiparişDurumu.Bekliyor => syaz "Bekliyor"
                _ => syaz "Diğer"
                _değer => syaz "Ad"
            }
            """);
        var select = Assert.IsType<SelectStatementSyntax>(Assert.Single(result.Root.Members));
        Assert.False(select.Arms[0].IsWildcard);
        Assert.True(select.Arms[1].IsWildcard);
        Assert.False(select.Arms[2].IsWildcard);
    }

    [Fact]
    public void ParsesObjectCreationCopyAndCurrentItem()
    {
        var result = Parsed.Clean("""
            a: Kullanıcı = @Kullanıcı
            b: Kullanıcı = @Kullanıcı("İbrahim", 38)
            c: Kullanıcı = @Kullanıcı { ad = "İbrahim" yaş = 38 }
            güncel: Kullanıcı = kullanıcı ile { ad = "Ahmet" }
            syaz ^.id
            """);
        Assert.IsType<ObjectCreationExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]).Initializers[0]);
        var called = Assert.IsType<ObjectCreationExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[1]).Initializers[0]);
        Assert.NotNull(called.Arguments);
        var initialized = Assert.IsType<ObjectCreationExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[2]).Initializers[0]);
        Assert.Equal(2, initialized.Initializers.Length);
        Assert.IsType<CopyExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[3]).Initializers[0]);
        var print = Assert.IsType<InvocationExpressionSyntax>(Assert.IsType<ExpressionStatementSyntax>(result.Root.Members[4]).Expression);
        var member = Assert.IsType<MemberAccessExpressionSyntax>(print.Arguments.Arguments[0].Expression);
        Assert.IsType<CurrentItemExpressionSyntax>(member.Expression);
    }

    [Fact]
    public void ParsesTryPreparationsMappingAndModules()
    {
        var prepared = Parsed.Clean("""
            başında
            {
            }
            dene
            {
            }
            yakala hata: VeritabanıHatası
            {
            }
            sonunda
            {
            }
            fırlat @Hata("Hata")
            """);
        var tryStatement = Assert.IsType<TryStatementSyntax>(prepared.Root.Members[0]);
        Assert.False(tryStatement.PreparationIsUnderscore);
        Assert.NotNull(tryStatement.Catches[0].Type);
        var hidden = Parsed.Clean("""
            _başında
            {
            }
            dene
            {
            }
            yakala hata
            {
            }
            """);
        Assert.True(Assert.IsType<TryStatementSyntax>(hidden.Root.Members[0]).PreparationIsUnderscore);
        var mapped = Parsed.Clean("""
            dönüşüm Kullanıcı => KullanıcıDto
            {
                id = ^.id
            }
            alan Ticaret.Api : genel
            al Ticaret.Modeller olarak Modeller
            """);
        Assert.IsType<MappingDeclarationSyntax>(mapped.Root.Members[0]);
        Assert.IsType<NamespaceDeclarationSyntax>(mapped.Root.Members[1]);
        Assert.IsType<ImportDeclarationSyntax>(mapped.Root.Members[2]);
    }

    [Fact]
    public void ParsesHostDataWebAndTestSyntax()
    {
        var result = Parsed.Clean("""
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            işlem
            {
                onayla
                gerial
            }
            servis KullanıcıServisi : kapsamlı
            {
            }
            bağla servis: KullanıcıServisi
            kontrolcü KullanıcıKontrolcü : "/kullanicilar"
            {
                rota GET "/{id}"
                afonksiyon bul(id: tamsayı): Kullanıcı?
                {
                    => bekle servis.bul(id)
                }
            }
            test "iki sayı doğru toplanır"
            {
                beklenen 1 == 1
            }
            genel olay kullanıcıEklendi: Kullanıcı
            eşişlem
            {
                ad: metin = bekle veriGetir()
            }
            bekle 2.saniye
            doğrula kullanıcı
            {
                kural kullanıcı.yaş >= 18 => "küçük"
            }
            """);
        Assert.IsType<DatabaseDeclarationSyntax>(result.Root.Members[0]);
        var transaction = Assert.IsType<TransactionStatementSyntax>(result.Root.Members[1]);
        Assert.IsType<CommitStatementSyntax>(transaction.Body.Statements[0]);
        Assert.IsType<RollbackStatementSyntax>(transaction.Body.Statements[1]);
        Assert.IsType<ServiceDeclarationSyntax>(result.Root.Members[2]);
        Assert.IsType<BindingStatementSyntax>(result.Root.Members[3]);
        var controller = Assert.IsType<ControllerDeclarationSyntax>(result.Root.Members[4]);
        var function = Assert.IsType<FunctionDeclarationSyntax>(controller.Members[0]);
        Assert.NotNull(function.Route);
        Assert.IsType<TestDeclarationSyntax>(result.Root.Members[5]);
        Assert.IsType<EventDeclarationSyntax>(result.Root.Members[6]);
        Assert.IsType<ConcurrentStatementSyntax>(result.Root.Members[7]);
        var wait = Assert.IsType<ExpressionStatementSyntax>(result.Root.Members[8]);
        var await = Assert.IsType<AwaitExpressionSyntax>(wait.Expression);
        Assert.IsType<MemberAccessExpressionSyntax>(await.Expression);
        Assert.IsType<ValidationStatementSyntax>(result.Root.Members[9]);
    }

    [Fact]
    public void ParsesDefaultParameterAndExpectedError()
    {
        var result = Parsed.Clean("""
            fonksiyon selamla(ad: metin, mesaj: metin = "Merhaba")
            {
            }
            beklenenHata
            {
                syaz "x"
            }
            """);
        var function = Assert.IsType<FunctionDeclarationSyntax>(result.Root.Members[0]);
        Assert.IsType<LiteralExpressionSyntax>(function.Parameters[1].DefaultValue);
        Assert.IsType<ExpectedErrorStatementSyntax>(result.Root.Members[1]);
    }

    [Fact]
    public void DurationStaysMemberAccess()
    {
        var result = Parsed.Clean("süre: süre = 30.dakika");
        var member = Assert.IsType<MemberAccessExpressionSyntax>(Assert.IsType<VariableDeclarationSyntax>(result.Root.Members[0]).Initializers[0]);
        Assert.Equal(Lexer.TokenKind.Dot, member.Operator.TokenKind);
    }

    [Fact]
    public void DumpNamesTheDeclaration()
    {
        var result = Parsed.Clean("ad: metin = \"İbrahim\"");
        var dump = SyntaxDumper.Dump(result.Root);
        Assert.Contains("CompilationUnit", dump, StringComparison.Ordinal);
        Assert.Contains("VariableDeclaration", dump, StringComparison.Ordinal);
    }
}
