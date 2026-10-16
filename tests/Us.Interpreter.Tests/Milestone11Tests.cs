using Us.Core;
using Us.Runtime;
using Us.Semantics;

namespace Us.Interpreter.Tests;

public class LifetimeTests
{
    [Fact]
    public async Task SingletonIsStableAcrossScopesAndTransientIsNot()
    {
        var singleton = Container(ServiceLifetime.Singleton);
        var first = singleton.Container.CreateScope();
        var second = singleton.Container.CreateScope();
        var a = await first.ResolveAsync(singleton.Type);
        var b = await first.ResolveAsync(singleton.Type);
        var c = await second.ResolveAsync(singleton.Type);
        Assert.Same(a.Reference, b.Reference);
        Assert.Same(a.Reference, c.Reference);
        first.Close();
        var d = await second.ResolveAsync(singleton.Type);
        Assert.Same(a.Reference, d.Reference);

        var transient = Container(ServiceLifetime.Transient);
        var scope = transient.Container.CreateScope();
        var left = await scope.ResolveAsync(transient.Type);
        var right = await scope.ResolveAsync(transient.Type);
        Assert.NotSame(left.Reference, right.Reference);
    }

    [Fact]
    public async Task ScopedIsSharedInsideOneScopeOnly()
    {
        var container = Container(ServiceLifetime.Scoped);
        var type = container.Type;
        var first = container.Container.CreateScope();
        var a = await first.ResolveAsync(type);
        var b = await first.ResolveAsync(type);
        Assert.Same(a.Reference, b.Reference);
        var second = container.Container.CreateScope();
        var c = await second.ResolveAsync(type);
        Assert.NotSame(a.Reference, c.Reference);
    }

    [Fact]
    public async Task FailedConstructionIsNotCached()
    {
        var type = new SourceTypeSymbol("Kutu", SourceTypeKind.Class);
        var calls = 0;
        var container = new UsContainer([new ServiceRegistration(type, ServiceLifetime.Singleton, null, default)], (_, _, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new UsRuntimeException(UsRuntimeCodes.DivisionByZero, "Sıfıra bölünemez.", 0, 0);
            }

            return new ValueTask<UsValue>(UsValue.FromReference(new object()));
        });
        var scope = container.CreateScope();
        var failed = await Assert.ThrowsAsync<UsRuntimeException>(async () => await scope.ResolveAsync(type));
        Assert.Equal(UsRuntimeCodes.DivisionByZero, failed.Code);
        var created = await scope.ResolveAsync(type);
        Assert.NotNull(created.Reference);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DuplicateRegistrationIsRejected()
    {
        var type = new SourceTypeSymbol("Kutu", SourceTypeKind.Class);
        var first = new ServiceRegistration(type, ServiceLifetime.Singleton, null, default);
        var second = new ServiceRegistration(type, ServiceLifetime.Transient, null, default);
        var error = Assert.Throws<UsRuntimeException>(() => new UsContainer([first, second], (_, _, _) => new ValueTask<UsValue>(UsValue.Unit)));
        Assert.Equal(UsRuntimeCodes.AmbiguousService, error.Code);
    }

    [Fact]
    public async Task ConcurrentSingletonAndScopedResolveOnce()
    {
        await AssertOne(ServiceLifetime.Singleton);
        await AssertOne(ServiceLifetime.Scoped);
        var transientCalls = 0;
        var transient = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var type = new SourceTypeSymbol("Kutu", SourceTypeKind.Class);
        var container = new UsContainer([new ServiceRegistration(type, ServiceLifetime.Transient, null, default)], async (_, _, _) =>
        {
            Interlocked.Increment(ref transientCalls);
            await transient.Task;
            return UsValue.FromReference(new object());
        });
        var scope = container.CreateScope();
        var left = scope.ResolveAsync(type);
        var right = scope.ResolveAsync(type);
        transient.SetResult();
        var pair = await Task.WhenAll(left.AsTask(), right.AsTask());
        Assert.Equal(2, transientCalls);
        Assert.NotSame(pair[0].Reference, pair[1].Reference);
    }

    private static async Task AssertOne(ServiceLifetime lifetime)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var type = new SourceTypeSymbol("Kutu", SourceTypeKind.Class);
        var container = new UsContainer([new ServiceRegistration(type, lifetime, null, default)], async (_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult();
            }

            await release.Task;
            return UsValue.FromReference(new object());
        });
        var scope = container.CreateScope();
        var first = scope.ResolveAsync(type);
        await started.Task;
        var second = scope.ResolveAsync(type);
        release.SetResult();
        var a = await first;
        var b = await second;
        Assert.Same(a.Reference, b.Reference);
        Assert.Equal(1, calls);
    }

    private static (UsContainer Container, SourceTypeSymbol Type) Container(ServiceLifetime lifetime)
    {
        var type = new SourceTypeSymbol("Kutu", SourceTypeKind.Class);
        return (new UsContainer([new ServiceRegistration(type, lifetime, null, default)], (_, _, _) => new ValueTask<UsValue>(UsValue.FromReference(new object()))), type);
    }
}

public class BindDiTests
{
    [Fact]
    public void BindingReadsTheContainerAndAtDoesNot()
    {
        var bound = Run("""
            servis Kutu : geçici { }
            k: Kutu = @Kutu()
            bağla servis: Kutu
            """);
        Assert.True(bound.Evaluated, Text(bound));
        Assert.Equal(1, bound.DirectConstructions);
        Assert.Equal(1, bound.Container!.Resolves);

        var onlyAt = Run("""
            servis Kutu : geçici { }
            k: Kutu = @Kutu()
            """);
        Assert.Equal(1, onlyAt.DirectConstructions);
        Assert.Equal(0, onlyAt.Container!.Resolves);
    }

    [Fact]
    public void MissingRegistrationIsUs1544()
    {
        var result = Run("""
            sınıf Kutu { }
            bağla servis: Kutu
            """);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.ServiceMissing);
    }

    [Fact]
    public void DirectCycleAndIndirectCycleReportTheChain()
    {
        var direct = Run("""
            servis A : geçici
            {
                oluştur(b: B) { }
            }
            servis B : geçici
            {
                oluştur(a: A) { }
            }
            bağla servis: A
            """);
        var directError = Assert.Single(direct.Diagnostics);
        Assert.Equal(DiagnosticCodes.ServiceCycle, directError.Code);
        Assert.Equal("A → B → A", directError.Message);

        var indirect = Run("""
            servis A : geçici
            {
                oluştur(b: B) { }
            }
            servis B : geçici
            {
                oluştur(c: C) { }
            }
            servis C : geçici
            {
                oluştur(a: A) { }
            }
            bağla servis: A
            """);
        var indirectError = Assert.Single(indirect.Diagnostics);
        Assert.Equal(DiagnosticCodes.ServiceCycle, indirectError.Code);
        Assert.Equal("A → B → C → A", indirectError.Message);
    }

    [Fact]
    public void ChainResolvesToTheLeaf()
    {
        var result = Run("""
            servis C : geçici
            {
                genel n: tamsayı
                oluştur() { n = 3 }
            }
            servis B : geçici
            {
                genel c: C
                oluştur(bağımlı: C) { c = bağımlı }
            }
            servis A : geçici
            {
                genel b: B
                oluştur(bağımlı: B) { b = bağımlı }
            }
            bağla servis: A
            syaz "{servis.b.c.n}"
            """);
        Assert.True(result.Evaluated, Text(result));
        Assert.Equal("3", Assert.Single(result.Output.Lines));
    }

    private static ExecutionResult Run(string text) => ProgramRunner.RunText("deneme.us", text);

    private static string Text(ExecutionResult result) => string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message));
}

public class ServiceDeclTests
{
    [Fact]
    public void MissingOrUnknownLifetimeIsUs0858AndMembersStayPrivate()
    {
        var missing = ProgramRunner.RunText("deneme.us", "servis Kutu { }");
        Assert.Contains(missing.Diagnostics, item => item.Code == DiagnosticCodes.MissingLifetime);
        Assert.False(missing.Evaluated);

        var unknown = ProgramRunner.RunText("deneme.us", "servis Kutu : yanlis { }");
        Assert.Contains(unknown.Diagnostics, item => item.Code == DiagnosticCodes.MissingLifetime);

        var ambiguous = UsCompile.Front(SourceFile.FromText("deneme.us", """
            servis Kutu : geçici
            {
                oluştur() { }
                oluştur(n: tamsayı) { }
            }
            """));
        Assert.Contains(ambiguous.Errors, item => item.Code == DiagnosticCodes.AmbiguousConstructor);

        var compiled = UsCompile.Front(SourceFile.FromText("deneme.us", """
            servis Kutu : geçici
            {
                fonksiyon gizli(): tamsayı => 1
            }
            """));
        Assert.Empty(compiled.Errors);
        Assert.Equal(Visibility.Private, compiled.Compilation.Services.Single().ServiceType.Methods.Single(method => method.Name == "gizli").Visibility);
    }

    [Fact]
    public void CaptiveSingletonCannotDependOnScoped()
    {
        var result = ProgramRunner.RunText("deneme.us", """
            servis Ortak : kapsamlı { }
            servis Kutu : tekil
            {
                oluştur(ortak: Ortak) { }
            }
            """);
        Assert.Contains(result.Diagnostics, item => item.Code == DiagnosticCodes.CaptiveDependency);
        Assert.False(result.Evaluated);
    }

    [Fact]
    public void SingletonMayKeepATransientAndScopedMayUseASingleton()
    {
        var transient = ProgramRunner.RunText("deneme.us", """
            servis Hızlı : geçici
            {
                genel n: tamsayı
                oluştur() { n = 4 }
            }
            servis Kutu : tekil
            {
                genel iç: Hızlı
                oluştur(bağımlı: Hızlı) { iç = bağımlı }
            }
            bağla servis: Kutu
            syaz "{servis.iç.n}"
            """);
        Assert.True(transient.Evaluated, string.Join(Environment.NewLine, transient.Diagnostics.Select(item => item.Message)));
        Assert.Equal("4", Assert.Single(transient.Output.Lines));
    }
}

public class HostStartTests
{
    [Fact]
    public async Task StartReadsConfigurationOnceAndResolvesAService()
    {
        var root = Path.Combine(Path.GetTempPath(), "us-m11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "uygulamaAyar.json"), """{ "MongoDb": { "ad": "kutu" } }""");
            var file = SourceFile.FromText(Path.Combine(root, "ana.us"), """
                servis Kutu : tekil
                {
                    genel n: tamsayı
                    oluştur() { n = 1 }
                }
                bağla servis: Kutu
                syaz "{servis.n}"
                """);
            var result = await UsApplicationHost.StartAsync(root, file);
            Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            Assert.Equal("1", Assert.Single(result.Output.Lines));
            Assert.Equal(1, result.Configuration!.Reads);
            Assert.NotNull(result.Container);
            Assert.True(result.Container!.Resolves >= 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class BindConfigTests
{
    [Fact]
    public async Task HostBindingUsesTheCachedSectionAndMissingSectionStaysUs1533()
    {
        var configuration = new MemoryConfiguration("""{ "MongoDb": { "ad": "kutu" } }""");
        var host = new UsHost { Configuration = configuration, Log = new ListLog() };
        var root = Path.Combine(Path.GetTempPath(), "us-m11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = SourceFile.FromText("deneme.us", """
                model Mongo : Ayar("MongoDb")
                {
                    ad: metin
                }
                bağla ayar: Mongo
                bağla ayar: Mongo
                """);
            var result = await UsApplicationHost.StartAsync(root, file, host);
            Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            Assert.Equal(1, configuration.Reads);
            Assert.Equal(2, host.Services.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        var missing = await UsApplicationHost.StartAsync(root, SourceFile.FromText("deneme.us", """
            model Mongo : Ayar("MongoDb") { ad: metin }
            bağla ayar: Mongo
            """), new UsHost { Configuration = new MemoryConfiguration("{ }") });
        Assert.Contains(missing.Diagnostics, item => item.Code == DiagnosticCodes.MissingSection);
    }
}

public class HostLogTests
{
    [Fact]
    public async Task LogWorksWithoutBindingAndBaglaLogDoesNotCompile()
    {
        var log = new ListLog();
        var root = Path.Combine(Path.GetTempPath(), "us-m11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await UsApplicationHost.StartAsync(root, SourceFile.FromText("deneme.us", """
                log.bilgi("merhaba")
                """), new UsHost { Log = log });
            Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            Assert.Contains(log.Records, item => item.Level == UsLogLevel.Information && item.Message == "merhaba");
        }
        finally
        {
            Directory.Delete(root, true);
        }

        var bound = ProgramRunner.RunText("deneme.us", "bağla log: metin");
        Assert.False(bound.Evaluated);
        Assert.Contains(bound.Diagnostics, item => item.Code == DiagnosticCodes.DuplicateName);
    }
}

public class ScopeDisposeTests
{
    [Fact]
    public async Task ClosedScopeRejectsResolveAndTheOldInstanceRejectsCalls()
    {
        var file = SourceFile.FromText("deneme.us", """
            servis Kutu : kapsamlı
            {
                genel fonksiyon ad(): metin => "k"
            }
            """);
        var compiled = UsCompile.Front(file);
        Assert.Empty(compiled.Errors);
        var evaluator = new Evaluator(new ListOutput());
        var instance = await evaluator.TakeAsync(compiled.Compilation, file, "Kutu");
        await evaluator.CallAsync(instance, "ad");
        evaluator.CloseScope();
        var closed = await Assert.ThrowsAsync<UsRuntimeException>(async () => await evaluator.CallAsync(instance, "ad"));
        Assert.Equal(DiagnosticCodes.ScopeClosed, closed.Code);
        var again = await Assert.ThrowsAsync<UsRuntimeException>(async () => await evaluator.Scope!.ResolveAsync(compiled.Compilation.Services[0].ServiceType));
        Assert.Equal(DiagnosticCodes.ScopeClosed, again.Code);
        var fresh = await evaluator.Container!.CreateScope().ResolveAsync(compiled.Compilation.Services[0].ServiceType);
        Assert.NotSame(instance, fresh.Reference);
    }

    [Fact]
    public async Task NativeTestsDoNotShareSingletons()
    {
        var result = await TestRunner.RunTextAsync("deneme.us", """
            sayaç: tamsayı = 0
            servis Kutu : tekil
            {
                oluştur() { sayaç = 1 }
            }
            test "bir"
            {
                bağla servis: Kutu
                beklenen sayaç == 1
            }
            test "iki"
            {
                bağla servis: Kutu
                beklenen sayaç == 1
            }
            """);
        Assert.Equal(2, result.Passed);
        Assert.Equal(0, result.Failed);
    }
}

public class ThreeConceptsTests
{
    [Fact]
    public void ImportConstructionAndBindingStaySeparate()
    {
        var mixed = ProgramRunner.RunText("deneme.us", """
            al json
            servis Kutu : geçici { }
            k: Kutu = @Kutu()
            bağla servis: Kutu
            """);
        Assert.True(mixed.Evaluated, string.Join(Environment.NewLine, mixed.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(1, mixed.DirectConstructions);
        Assert.Equal(1, mixed.Container!.Resolves);

        var imported = ProgramRunner.RunText("deneme.us", "al json");
        Assert.True(imported.Evaluated, string.Join(Environment.NewLine, imported.Diagnostics.Select(item => item.Message)));
        Assert.Equal(0, imported.DirectConstructions);
        Assert.Equal(0, imported.Container!.Resolves);
    }

    [Fact]
    public async Task ServiceUsesHostConfigLogQueryEventsAndCaughtErrors()
    {
        var log = new ListLog();
        var configuration = new MemoryConfiguration("""{ "MongoDb": { "ad": "kutu" } }""");
        var result = ProgramRunner.RunText("deneme.us", """
            model Mongo : Ayar("MongoDb")
            {
                ad: metin
            }
            bağla ayar: Mongo
            servis Kutu : geçici
            {
                genel olay oldu: metin
                genel fonksiyon yaz(): metin
                {
                    log.bilgi("servisten")
                    oldu += kaydet
                    oldu.tetikle("a")
                    => "oldu"
                }
                genel fonksiyon kaydet(değer: metin) { }
                genel fonksiyon eşik(değerler: liste<tamsayı>): tamsayı
                {
                    => değerler.filtrele(^ > 1).ilk()
                }
                genel fonksiyon patlat()
                {
                    fırlat @Hata("servis")
                }
                genel afonksiyon getir(): tamsayı
                {
                    bekle 0.milisaniye
                    => 7
                }
                genel fonksiyon arttır(ref değer: tamsayı)
                {
                    değer = değer + 1
                }
            }
            bağla servis: Kutu
            iz: metin = servis.yaz()
            syaz "{servis.eşik([1, 2, 3])}"
            n: tamsayı = bekle servis.getir()
            syaz "{n}"
            dene
            {
                servis.patlat()
            }
            yakala hata: Hata
            {
                syaz hata.mesaj
            }
            değer: tamsayı = 1
            servis.arttır(ref değer)
            syaz "{değer}"
            """, new UsHost { Configuration = configuration, Log = log });
        Assert.True(result.Evaluated, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + " " + item.Message)));
        Assert.Equal(["2", "7", "servis", "2"], result.Output.Lines);
        Assert.Contains(log.Records, item => item.Message == "servisten");
        Assert.Equal(1, configuration.Reads);

        var nested = await TestRunner.RunTextAsync("deneme.us", """
            servis Kutu : geçici
            {
                genel fonksiyon n(): tamsayı => 4
            }
            test "servis"
            {
                bağla servis: Kutu
                beklenen servis.n() == 4
            }
            """);
        Assert.Equal(1, nested.Passed);
    }
}
