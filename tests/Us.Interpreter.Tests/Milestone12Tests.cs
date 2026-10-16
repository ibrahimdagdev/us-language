using System.Text.Json;
using Us.Core;
using Us.Interpreter;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

file static class Db
{
    public const string Users = """
        model Kullanıcı
        {
            id: tamsayı : anahtar
            ad: metin
            aktif: mantıksal
        }
        veritabanı UygulamaDb
        {
            tablo kullanıcılar: Kullanıcı
        }
        bağla db: UygulamaDb
        """;

    public const string Orders = """
        model Kullanıcı
        {
            id: tamsayı : anahtar
            ad: metin
        }
        model Sipariş
        {
            id: tamsayı : anahtar
            kullanıcı: Kullanıcı
        }
        veritabanı UygulamaDb
        {
            tablo kullanıcılar: Kullanıcı
            tablo siparişler: Sipariş
        }
        bağla db: UygulamaDb
        """;

    public static ExecutionResult Run(string text) => global::Us.Interpreter.Tests.Run.Clean(text);

    public static IEnumerable<string> Constants(BoundNode node)
    {
        if (node.Constant is not null)
        {
            yield return node.Constant;
        }

        foreach (var child in node.Children)
        {
            foreach (var value in Constants(child))
            {
                yield return value;
            }
        }
    }
}

public class MemoryDatabaseTests
{
    [Fact]
    public void TwoTablesAreSeparateStoresAndKnowTheModel()
    {
        var bound = Binder.Bind(SourceFile.FromText("deneme.us", Db.Users.Replace("aktif: mantıksal", "aktif: mantıksal\n") + """

            model Ürün
            {
                id: tamsayı : anahtar
            }
            """));
        Assert.False(bound.Diagnostics.HasErrors, string.Join(Environment.NewLine, bound.Diagnostics.ToImmutableArray().Select(item => item.Message)));
        var database = Assert.Single(bound.Databases);
        var product = new TableSymbol("ürünler", new SourceTypeSymbol("Ürün", SourceTypeKind.Model), database);
        database.Tables.Add(product);
        var memory = new MemoryDatabase(bound.Databases);
        var users = memory.Open(database.Tables[0]);
        var products = memory.Open(product);
        Assert.NotSame(users, products);
        Assert.Equal("Kullanıcı", users.Model.Name);
        Assert.Equal(string.Empty, memory.LastPlan);
        Assert.DoesNotContain("SELECT", memory.LastPlan, StringComparison.Ordinal);
        var missing = new TableSymbol("yok", users.Model, database);
        var exception = Assert.Throws<UsRuntimeException>(() => memory.Open(missing));
        Assert.Equal(UsRuntimeCodes.UnknownTable, exception.Code);
    }

    [Fact]
    public void OpenTransactionCountReturnsToZero()
    {
        var database = new DatabaseSymbol("UygulamaDb");
        var memory = new MemoryDatabase([database]);
        memory.Begin();
        Assert.Equal(TransactionState.Active, memory.State);
        Assert.Equal(1, memory.OpenTransactions);
        memory.Commit();
        Assert.Equal(TransactionState.Committed, memory.State);
        Assert.Equal(0, memory.OpenTransactions);
    }
}

public class TableQueryTests
{
    [Fact]
    public void ActiveUsersAreFilteredAndSortedWithoutSql()
    {
        var result = Db.Run(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Şeyma" aktif = doğru }
            b: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            c: Kullanıcı = @Kullanıcı { ad = "Zeynep" aktif = yanlış }
            bekle db.kullanıcılar.ekle(a)
            bekle db.kullanıcılar.ekle(b)
            bekle db.kullanıcılar.ekle(c)
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.aktif).sırala(^.ad).tümü()
            syaz "{satırlar.adet}"
            dön satırlar -> kişi
            {
                syaz kişi.ad
            }
            """);
        Assert.Equal(["2", "Ali", "Şeyma"], result.Output.Lines);
        var bound = Binder.Bind(SourceFile.FromText("deneme.us", Db.Users + "\nbekle db.kullanıcılar.filtrele(^.aktif).sırala(^.ad).tümü()\n"));
        var constants = bound.Members.SelectMany(Db.Constants).ToArray();
        Assert.Contains("filtrele", constants);
        Assert.Contains("sırala", constants);
        Assert.Contains("tümü", constants);
        Assert.DoesNotContain(constants, item => item.Contains("SELECT", StringComparison.Ordinal) || item.Contains("WHERE", StringComparison.Ordinal));
    }

    [Fact]
    public void CompoundFilterAndCapturedParameterStayValues()
    {
        var result = Db.Run(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Ali' OR 1=1" aktif = doğru }
            b: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            bekle db.kullanıcılar.ekle(b)
            aranan: metin = "Ali' OR 1=1"
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.filtrele(^.aktif && ^.ad == aranan).tümü()
            syaz "{satırlar.adet}"
            dön satırlar -> kişi
            {
                syaz kişi.ad
            }
            """);
        Assert.Equal(["1", "Ali' OR 1=1"], result.Output.Lines);
    }

    [Fact]
    public void NullableTurkishDecimalAndDateParametersMatchTheRow()
    {
        var result = Db.Run("""
            model Kayıt
            {
                id: tamsayı : anahtar
                ad: metin
                not: metin?
                tutar: hondalık
                gün: tarih
            }
            veritabanı UygulamaDb
            {
                tablo kayıtlar: Kayıt
            }
            bağla db: UygulamaDb
            boş: Kayıt = @Kayıt { ad = "Şeyma" not = ~ tutar = 12 gün = bugün }
            dolu: Kayıt = @Kayıt { ad = "Ali" not = "var" tutar = 7 gün = bugün }
            bekle db.kayıtlar.ekle(boş)
            bekle db.kayıtlar.ekle(dolu)
            aranan: metin? = ~
            beklenenTutar: hondalık = 12
            boşlar: liste<Kayıt> = bekle db.kayıtlar.filtrele(^.not == aranan && ^.tutar == beklenenTutar && ^.gün == bugün).tümü()
            syaz "{boşlar.adet}"
            dön boşlar -> kayıt
            {
                syaz kayıt.ad
            }
            """);
        Assert.Equal(["1", "Şeyma"], result.Output.Lines);
    }

    [Fact]
    public void DescendingSortReversesTheOrder()
    {
        var result = Db.Run(Db.Users + """

            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Şeyma" aktif = doğru })
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tsırala(^.ad).tümü()
            dön satırlar -> kişi
            {
                syaz kişi.ad
            }
            """);
        Assert.Equal(["Şeyma", "Ali"], result.Output.Lines);
    }

    [Fact]
    public void UserFunctionInPredicateIsRejected()
    {
        var result = Run.Text(Db.Users + """

            fonksiyon yetişkin(kişi: Kullanıcı): mantıksal => doğru
            bekle db.kullanıcılar.filtrele(yetişkin(^)).tümü()
            """);
        Assert.False(result.Evaluated);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.QueryEffect);
    }
}

public class IncludeTests
{
    [Fact]
    public void FlatIncludeFillsTheRelatedModel()
    {
        var result = Db.Run(Db.Orders + """

            kişi: Kullanıcı = @Kullanıcı { ad = "Ada" }
            bekle db.kullanıcılar.ekle(kişi)
            sipariş: Sipariş = @Sipariş { kullanıcı = kişi }
            bekle db.siparişler.ekle(sipariş)
            satırlar: liste<Sipariş> = bekle db.siparişler.birlikte(^.kullanıcı).tümü()
            dön satırlar -> satır
            {
                syaz satır.kullanıcı.ad
            }
            """);
        Assert.Equal(["Ada"], result.Output.Lines);
    }

    [Fact]
    public void MissingIncludeMemberIsUs0862()
    {
        var result = Run.Text(Db.Orders + "\nbekle db.siparişler.birlikte(^.yok).tümü()\n");
        Assert.False(result.Evaluated);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.IncludeMember, diagnostic.Code);
    }
}

public class NestedIncludeTests
{
    [Fact]
    public void NestedPathSaysThisVersionDoesNotSupportIt()
    {
        var result = Run.Text(Db.Orders + "\nbekle db.siparişler.birlikte(^.kullanıcı.adres).tümü()\n");
        Assert.False(result.Evaluated);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.NestedInclude, diagnostic.Code);
        Assert.Contains("bu sürümde desteklenmiyor", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlatPathDoesNotUseTheNestedCode()
    {
        var result = Db.Run(Db.Orders + "\nbekle db.siparişler.birlikte(^.kullanıcı).tümü()\n");
        Assert.Empty(result.Diagnostics);
    }
}

public class TableWriteTests
{
    [Fact]
    public void InsertWritesTheGeneratedKey()
    {
        var result = Db.Run(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            b: Kullanıcı = @Kullanıcı { ad = "Ayşe" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            bekle db.kullanıcılar.ekle(b)
            syaz "{a.id}"
            syaz "{b.id}"
            """);
        Assert.Equal(["1", "2"], result.Output.Lines);
    }

    [Fact]
    public void UpdateReplacesTheRowAndMissingUpdateIsUs1548()
    {
        var updated = Db.Run(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            a.ad = "Veli"
            bekle db.kullanıcılar.güncelle(a)
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            dön satırlar -> kişi
            {
                syaz kişi.ad
            }
            """);
        Assert.Equal(["Veli"], updated.Output.Lines);
        var missing = Run.Text(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Yok" aktif = yanlış }
            bekle db.kullanıcılar.güncelle(a)
            """);
        Assert.True(missing.Evaluated);
        Assert.Equal(UsRuntimeCodes.MissingRecord, Assert.Single(missing.Diagnostics).Code);
    }

    [Fact]
    public void DeleteRemovesTheRowAndMissingDeleteIsUs1548()
    {
        var deleted = Db.Run(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Ali" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            bekle db.kullanıcılar.sil(a)
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["0"], deleted.Output.Lines);
        var missing = Run.Text(Db.Users + """

            a: Kullanıcı = @Kullanıcı { ad = "Yok" aktif = yanlış }
            bekle db.kullanıcılar.sil(a)
            """);
        Assert.Equal(UsRuntimeCodes.MissingRecord, Assert.Single(missing.Diagnostics).Code);
    }

    [Fact]
    public void ValidationStopsTheWrite()
    {
        var result = Run.Text("""
            model Kullanıcı
            {
                id: tamsayı : anahtar
                ad: metin : zorunlu, uzunluk(3)
                aktif: mantıksal
            }
            veritabanı UygulamaDb
            {
                tablo kullanıcılar: Kullanıcı
            }
            bağla db: UygulamaDb
            a: Kullanıcı = @Kullanıcı { ad = "" aktif = doğru }
            bekle db.kullanıcılar.ekle(a)
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.True(result.Evaluated);
        Assert.Equal(UsRuntimeCodes.WriteRejected, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(result.Output.Lines);
    }
}

public class AutoCommitTests
{
    [Fact]
    public void SuccessfulBodyPersistsWithoutOnayla()
    {
        var result = Db.Run(Db.Users + """

            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["1"], result.Output.Lines);
    }

    [Fact]
    public void ExplicitOnaylaPersists()
    {
        var result = Db.Run(Db.Users + """

            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                onayla
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["1"], result.Output.Lines);
    }

    [Fact]
    public void TransactionWithoutADatabaseStillRuns()
    {
        var result = Db.Run("""
            işlem
            {
                onayla
            }
            syaz "bitti"
            """);
        Assert.Equal(["bitti"], result.Output.Lines);
    }
}

public class RollbackTests
{
    [Fact]
    public void GerialDropsTheRowAndLaterWritesStay()
    {
        var result = Db.Run(Db.Users + """

            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                gerial
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ayşe" aktif = doğru })
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            dön satırlar -> kişi
            {
                syaz kişi.ad
            }
            """);
        Assert.Equal(["1", "Ayşe"], result.Output.Lines);
    }

    [Fact]
    public void UserErrorRollsBackAndPropagates()
    {
        var result = Db.Run(Db.Users + """

            dene
            {
                işlem
                {
                    bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                    fırlat @Hata("x")
                }
            }
            yakala hata: Hata
            {
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["0"], result.Output.Lines);
    }

    [Fact]
    public void FinallyThatThrowsStillRollsBack()
    {
        var result = Db.Run(Db.Users + """

            dene
            {
                işlem
                {
                    bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                    dene
                    {
                        fırlat @Hata("x")
                    }
                    yakala hata: Hata
                    {
                    }
                    sonunda
                    {
                        fırlat @Hata("y")
                    }
                }
            }
            yakala hata: Hata
            {
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["0"], result.Output.Lines);
    }

    [Fact]
    public void NestedTransactionIsRejected()
    {
        var result = Run.Text("""
            işlem
            {
                işlem
                {
                }
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Equal(DiagnosticCodes.NestedTransaction, Assert.Single(result.Diagnostics).Code);
    }
}

public class ClosedTransactionTests
{
    [Fact]
    public void SecondOnaylaIsABindErrorAndDoesNotRun()
    {
        var result = Run.Text(Db.Users + """

            işlem
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                onayla
                onayla
            }
            """);
        Assert.False(result.Evaluated);
        Assert.Equal(DiagnosticCodes.TransactionClosed, Assert.Single(result.Diagnostics).Code);
        Assert.Empty(result.Output.Lines);
    }

    [Fact]
    public void RuntimeReachesASecondTerminalAsUs1549AndKeepsTheCommit()
    {
        var result = Db.Run(Db.Users + """

            dene
            {
                işlem
                {
                    bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                    dön 1 ile 2 -> i
                    {
                        onayla
                    }
                }
            }
            yakala hata
            {
            }
            satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
            syaz "{satırlar.adet}"
            """);
        Assert.Equal(["1"], result.Output.Lines);
    }

    [Fact]
    public async Task ClosedScopeRejectsDatabaseUse()
    {
        var evaluator = new Evaluator(new ListOutput());
        var file = SourceFile.FromText("deneme.us", Db.Users);
        var compiled = UsCompile.Front(file);
        Assert.Empty(compiled.Errors);
        await evaluator.ExecuteAsync(compiled.Compilation, file);
        var exception = Assert.Throws<UsRuntimeException>(() => evaluator.RequireDatabase(new TextSpan(0, 1)));
        Assert.Equal(UsRuntimeCodes.ScopeClosed, exception.Code);
    }

    [Fact]
    public async Task TestsDoNotShareDatabaseRows()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", Db.Users + """

            test "bir"
            {
                bekle db.kullanıcılar.ekle(@Kullanıcı { ad = "Ali" aktif = doğru })
                satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
                beklenen satırlar.adet == 1
            }
            test "iki"
            {
                satırlar: liste<Kullanıcı> = bekle db.kullanıcılar.tümü()
                beklenen satırlar.adet == 0
            }
            """);
        Assert.True(result.Compiled, string.Join(" | ", result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(2, result.Passed);
    }
}

public class SchemaDiffTests
{
    [Fact]
    public void AddingAFieldIsWrittenOnceAndTheNextRunIsEmpty()
    {
        var root = NewRoot();
        try
        {
            Write(root, """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            var created = SyncDatabase.Run(root, false);
            Assert.True(created.Applied);
            Assert.Contains(created.Steps, step => step.Kind == SchemaStepKind.CreateTable);
            Write(root, """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                    ad: metin
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            var added = SyncDatabase.Run(root, false);
            Assert.True(added.Applied);
            Assert.Equal(SchemaStepKind.AddColumn, Assert.Single(added.Steps).Kind);
            var before = File.ReadAllText(SyncDatabase.FilePath(root));
            var again = SyncDatabase.Run(root, false);
            Assert.Empty(again.Steps);
            Assert.False(again.Applied);
            Assert.Equal(before, File.ReadAllText(SyncDatabase.FilePath(root)));
            var document = JsonSerializer.Deserialize<SchemaDocument>(before);
            Assert.NotNull(document);
            Assert.Equal(1, document.History.Count(item => item.Kind == nameof(SchemaStepKind.AddColumn)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EmptyFolderWritesNothing()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        try
        {
            var result = SyncDatabase.Run(root, false);
            Assert.Empty(result.Steps);
            Assert.False(File.Exists(SyncDatabase.FilePath(root)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "us-m12-" + Guid.NewGuid().ToString("N"));

    private static void Write(string root, string text)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "ana.us"), text);
    }
}

public class DestructiveSchemaTests
{
    [Fact]
    public void DropAndNarrowWaitForApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-m12-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                    ad: metin
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            Assert.True(SyncDatabase.Run(root, false).Applied);
            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: tamsayı : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            var blocked = SyncDatabase.Run(root, false);
            Assert.False(blocked.Applied);
            Assert.Equal(DiagnosticCodes.DestructiveSchema, Assert.Single(blocked.Diagnostics).Code);
            Assert.Contains(blocked.Steps, step => step.Kind == SchemaStepKind.DropColumn);
            var kept = JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(SyncDatabase.FilePath(root)));
            Assert.Contains(kept!.Tables[0].Columns, column => column.Name == "ad");
            var approved = SyncDatabase.Run(root, true);
            Assert.True(approved.Applied);
            var dropped = JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(SyncDatabase.FilePath(root)));
            Assert.DoesNotContain(dropped!.Tables[0].Columns, column => column.Name == "ad");
            Assert.Contains("Geri alma notu", File.ReadAllText(SyncDatabase.FilePath(root)), StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(root, "ana.us"), """
                model Kullanıcı
                {
                    id: metin : anahtar
                }
                veritabanı UygulamaDb
                {
                    tablo kullanıcılar: Kullanıcı
                }
                """);
            var narrow = SyncDatabase.Run(root, false);
            Assert.False(narrow.Applied);
            Assert.Equal(DiagnosticCodes.DestructiveSchema, Assert.Single(narrow.Diagnostics).Code);
            Assert.Contains(narrow.Steps, step => step.Kind == SchemaStepKind.NarrowColumn);
            var unchanged = JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(SyncDatabase.FilePath(root)));
            Assert.Equal("tamsayı", unchanged!.Tables[0].Columns.Single(column => column.Name == "id").Type);
            Assert.True(SyncDatabase.Run(root, true).Applied);
            var changed = JsonSerializer.Deserialize<SchemaDocument>(File.ReadAllText(SyncDatabase.FilePath(root)));
            Assert.Equal("metin", changed!.Tables[0].Columns.Single(column => column.Name == "id").Type);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
