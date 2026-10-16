# Compiler Parity Matrix

Yaşayan kaynak: Interpreter semantiği ile `DotNetEmitBackend` emit yüzeyi.

Durum seviyeleri (yalnız bunlar):

| Seviye | Anlam |
| --- | --- |
| **SUPPORTED** | Interpreter ile parity doğrulanmış; `us derle` production-supported yüzey |
| **PARTIAL** | Kısmi emit; production-supported değil; sessiz yanlış derleme yasak |
| **DEFERRED** | M15.5 sonrası (M16.5); henüz emit yüzeyi yok veya kapı geçilmedi |

`N/A` yalnızca dil dışı / milestone dışı engeller (ör. SQL Server, Kestrel) için kullanılır.

## v0.1 compiler contract

`us derle` yalnız bu matriste **SUPPORTED** olarak işaretlenen dil yüzeyi için production-supported kabul edilir.

**PARTIAL** veya **DEFERRED** bir özellik kullanılırsa derleyici bunu sessizce yanlış derlemez.

Davranış:

1. doğru compile (yalnız SUPPORTED ve o kullanım için gerçekten desteklenen alt küme), veya
2. structured compiler diagnostic (ör. US1900 veya daha özgül kod).

Interpreter fallback yasaktır: üretilen assembly `Evaluator`, `ProgramRunner`, gömülü kaynak yeniden parse/bind yolu kullanamaz.

## SUPPORTED

| Feature | Parity Test | Runtime Helper | US1900 | Known Limitation |
| --- | --- | --- | --- | --- |
| Parity harness | ParityHarnessTests | EmittedProcess | no | — |
| metin + syaz/yaz + interpolasyon | ParityHarnessTests | UsEmitOps.Display | no | — |
| Skaler tip map / UsValue storage | ClrTypeMapTests | UsValue | no | CLR storage = UsValue |
| Literals (int/uint/float/decimal/bool/char/metin/~) | LiteralEmitParityTests | UsEmitOps | no | — |
| Locals + `=` | LocalAssignmentParityTests | UsEmitOps.Store | no | — |
| `+=` `++`/`--` | CompoundAssignParityTests | UsMath | no | Event `+=` değil |
| Unary/binary aritmetik | ArithmeticParityTests | UsMath | no | — |
| Karşılaştırma / `&&` `||` / `??` | LogicParityTests | UsMath / UsEmitOps | no | — |
| Nullable skaler | NullableParityTests | UsEmitOps | no | — |
| `eğer` / expression-if | IfParityTests | — | no | — |
| `tekrarla` / `yap` / `sonsuzdön` / `dön .. ile` / `çık` `devam` | LoopParityTests | UsEmitOps.RequireInt | no | — |
| User functions + return + recursion | PhaseBParityTests | — | no | — |
| Default parameters | DefaultArgParityTests | — | no | — |
| Model construct + fields + copy | ModelFieldParityTests | UsEmitObject | no | — |
| `liste` literal / `adet` / index | ArrayParityTests | UsEmitList | no | — |
| `filtrele` / `seç` / `ekle` / `ilk` + `^` | QueryParityTests | UsEmitList + lambda IL | no | — |
| `fırlat` / `dene`/`yakala`/`sonunda` | ThrowParityTests | UsEmitOps.Throw | no | — |
| Multi-file real-world proof | RealWorldCompilationProofTests | Us.Runtime helpers | no | Async yok |
| Interpreter fallback guard | ArchitectureGuardTests | — | no | — |
| `afonksiyon` / `bekle` (gerçek suspension) | AwaitParityTests | UsEmitAsync / UsAsyncBox | no | Source'ta Task yok |
| Üst düzey `bekle` | TopLevelAwaitParityTests | UsEmitAsync.RunEntry | no | void Main köprüsü |
| `eşişlem` + aggregate hata (SOURCE ORDER) | ConcurrentParityTests | UsEmitAsync.ConcurrentOnSlots | no | — |
| `olay` / `+=` / `-=` / `.tetikle` | EventParityTests | UsEmitEvents / UsEmitVTable | no | CLR event değil |
| Phase F gate (async+eşişlem+olay) | PhaseFGateTests | UsEmitAsync + UsEmitEvents | no | — |
| Instance methods (olay/sınıf) | EventParityTests | static `(self,…)` IL | no | — |
| Instance `afonksiyon` / `bu` / nested bekle | AsyncInstanceParityTests | UsEmitAsync + ThisSlot | no | async+ByRef US1900; type-level `statik afonksiyon` not emitted |
| `bugün` / `şimdi` + süre aritmetiği | StdlibParityTests | UsEmitHost / UsEmitStdlib / UsEmitAsync.Duration | no | FixedClock via `US_TEST_CLOCK` |
| `json.yaz` / `json.oku` (model + nested + `liste<model>`) | JsonParityTests / StdlibParityTests | UsEmitJson + UsEmitObject | no | symbol-driven shape; US1541/US1529; tarih/saat JSON N/A |
| `dosya` / `yol` / `klasör` | StdlibParityTests | UsEmitStdlib + MemoryFileSystem | no | process-local FS |
| `log.*` | StdlibParityTests | UsEmitStdlib.Log | no | stdout'a yazmaz; ListLog |
| metin üyeleri (kırp/büyük/…) | StdlibParityTests | UsEmitStdlib tr-TR | no | — |
| `bağla ayar` | StdlibParityTests | UsEmitStdlib.BindSetting | no | US_TEST_CONFIG |
| DI `bağla servis` / lifetimes / ctor deps / US1546 cycle | DiDbParityTests | UsEmitDi | no | `@` ≠ container resolve |
| MemoryDatabase `ekle`/`tümü` + `bağla db` | DiDbParityTests | UsEmitDatabase | no | Memory default |
| Tablo `filtrele` / `sırala` / `tsırala` / `güncelle` / `sil` | DiDbParityTests | UsEmitDatabase + UsQueryExpr | no | Structured IR preferred; dış yerel → delegate (memory-only) |
| Tablo `birlikte` (flat include) | DiDbParityTests / UsEmitDatabaseTests | UsEmitDatabase.Include | no | İlişki include, JOIN değil; iç içe US0863 |
| SQL Server provider (parameterized CRUD/query/tx/schema) | SqlServerUnitTests | Us.Data.SqlServer | no | Live: `US_TEST_SQLSERVER_CONNECTION`; unit translation always |
| `işlem` / `onayla` / `gerial` / auto-commit / rollback-on-error | DiDbParityTests | UsEmitDatabase.Finish | no | M12: gerial sonrası body devam |
| Outbound `http.*` + FakeHttpTransport | HttpParityTests | UsEmitHttp + US_TEST_HTTP | no | Fake default; `HttpClientTransport` via `US_HTTP_CLIENT=1` / `UseHttpClientTransport()` |
| Compiled route handler (in-process) | HttpParityTests | UsEmitHttp.Dispatch + US_TEST_ROUTE | no | CI test dispatch without sockets |
| Production Kestrel listener | KestrelHostTests / KestrelParityTests | Us.Hosting.UsKestrelHost | no | Real TCP; default `http://127.0.0.1:5080`; `US_URLS` / `US_PORT`; HTTPS not in M26 |
| Phase G gate | PhaseGGateTests | UsEmit* helpers | no | — |
| Advanced real-world compiled proof | AdvancedRealWorldParityTests | separate process | no | Evaluator/ProgramRunner yok |
| `ref` / `çıktı` (yerel / parametre) | RefOutParityTests | UsValue& ByRef IL | no | Alan hedefi ve async+ByRef US1900 |
| Local package resolve/cache (`paketler/`, US1554–59) | PackageResolutionTests / ZipSlipTests | PackageResolver + PackageCache | no | Exact versions; offline; no public registry |
| Memory full-stack acceptance (Kestrel + DB + JSON) | M28ReleaseAcceptanceTests | Us.Hosting + UsEmitDatabase | no | `samples/BackendAcceptance`; SQL live optional via env |

## PARTIAL

| Feature | Parity Test | Runtime Helper | US1900 | Known Limitation |
| --- | --- | --- | --- | --- |
| `oku` / `sor` / `temizle` | ConsoleIntrinsicParityTests | UsEmitOps | varies | stdin parity ham |
| `?.` member | — | — | varies | OOP derinliği |
| foreach koleksiyon dön | — | — | varies | range dön Supported |
| Class / interface / override / virtual | — | UsEmitObject | varies | Instance methods limited |
| Generics depth | — | — | varies | Basit kullanım dışında |
| küme / sözlük / sliste | — | UsEmitList | varies | liste Supported |
| Full query surface | QueryParityTests (alt küme) | UsEmitList | varies | filtrele/seç/ekle/ilk Supported |
| `başında` / `_başında` | — | — | varies | dene/yakala/sonunda Supported |
| tarih / saat literal | — | — | varies | skaler literaller Supported; clock/duration SUPPORTED |
| Tablo alan doğrulama (uzunluk/benzersiz/aralık) | — | UsEmitDatabase | varies | zorunlu + anahtar Supported |

PARTIAL satırlar production-supported değildir. Emit yoksa veya güvenli değilse structured diagnostic gerekir.

## DEFERRED

M16.5 COMPLETE. M20–M27 COMPLETE (`ref`/`çıktı`, JSON nested, async instance, tablo sorgu/`birlikte`, SQL Server provider, Kestrel host, local package resolution). Kalan:

| Feature | Task | Reason |
| --- | --- | --- |
| Alan `ref`/`çıktı` hedefi | TASK-277 narrow | Heap alan adresi yok; US1900 |
| Async + `ref`/`çıktı` | TASK-278 | CLR async ByRef; US1900 |
| Public package registry server | post-M27 | Client stubs only; no hosted registry |
| SemVer ranges / lockfile | post-M27 | Exact versions only in v0.2 |
| HTTPS / certificate management | post-M26 | HTTP listener sufficient for M26 |
| tarih/saat/süre SQL mapping | M25 narrow | Scalar query types SUPPORTED; clock types gated |
| Live SQL Server E2E | M25/M28 | Unit-validated; live needs `US_TEST_SQLSERVER_CONNECTION` |

## Invariants

- BoundCompilation → DotNetEmitBackend only.
- Semantics reference: Interpreter.
- Helpers: `Us.Runtime` (`UsEmitOps`, `UsEmitObject`, `UsEmitList`, `UsMath`, `UsEmitAsync`, `UsEmitEvents`, `UsEmitHost`, `UsEmitStdlib`, `UsEmitJson`, `UsEmitDi`, `UsEmitDatabase`, `UsEmitHttp`) — interpreter wrapper değil.
- SUPPORTED satırda normal kullanım US1900 üretmez.
- Generated assembly: Evaluator / ProgramRunner / runtime parse-rebind yok.
