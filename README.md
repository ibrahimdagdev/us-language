# US Programlama Dili

US, Türkçe sözdizimli bir programlama dilidir. v0.2 .NET derleyici backend, CLI, Kestrel barındırma, SQL Server sağlayıcısı ve yerel paket çözümlemesini içerir.

Ürün sürümü: **0.2.0**

## Gereksinimler

- .NET 10 SDK

## Hızlı başlangıç

```text
dotnet build Us.slnx
dotnet run --project src/Us.Cli -- yeni konsol Merhaba
cd Merhaba
dotnet run --project ../src/Us.Cli -- çalıştır
```

Depo kökünden örnek:

```text
dotnet run --project src/Us.Cli -- çalıştır samples/Merhaba/ana.us
```

Beklenen çıktı: `Merhaba US!`

## CLI

Windows release paketinde public komut `us` şeklindedir (`us.exe`). Kurulum:

```text
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Kurulum `%LOCALAPPDATA%\US\bin` altına yapılır ve USER PATH'e eklenir (yönetici gerekmez). ZIP içindeki `install.cmd` çift tıklanabilir. VS Code kuruluysa `us-language-0.2.0.vsix` otomatik kurulur. Yeni terminalde `us --version` çalıştırın.

Kaynak ağaçtan:

```text
dotnet run --project src/Us.Cli -- <komut>
```

| Komut | Açıklama |
| --- | --- |
| `us yeni konsol Ad` | Yeni konsol projesi |
| `us çalıştır` | Yorumlayıcı ile çalıştır |
| `us derle` | `DotNetEmitBackend` ile DLL üret |
| `us test` | `test` gövdelerini çalıştır |
| `us yayınla` | `yayin/` altına yayın çıktısı |
| `us biçimlendir` | Kaynak biçimlendir |
| `us paket ekle <ad> [sürüm]` | Manifest'e paket satırı ekle |
| `us paket kur <arsiv.zip>` | Yerel paket arşivini kur |
| `us veritabanı senkronize [onay]` | Şema eşitle |
| `us --version` | Ürün sürümü (`0.2.0`) |

## Dil özeti

Değişkenler, `eğer`, döngüler, fonksiyonlar, model/sınıf, koleksiyon/`^` sorgu, `dene`/`yakala`, `afonksiyon`/`bekle`, `eşişlem`, olaylar, JSON/dosya/log, DI (`bağla`), `MemoryDatabase` + `işlem`, `kontrolcü`/`rota`, gerçek Kestrel dinleyicisi, SQL Server sağlayıcısı (birim + canlı doğrulanmış), yerel paket çözümlemesi.

Ayrıntı: `docs/language-spec.md`.

## Derleyici desteği

`us derle` yalnız `docs/compiler-parity.md` içinde **SUPPORTED** işaretli yüzey için production-supported kabul edilir. PARTIAL/DEFERRED özellikler sessizce yanlış derlenmez; structured tanı (ör. US1900) üretir. Interpreter fallback yoktur.

## Bilinen sınırlar

- SQL Server sağlayıcısı birim ve canlı E2E ile doğrulanmıştır; varsayılan `dotnet test` canlı SQL’i yalnız `US_TEST_SQLSERVER_CONNECTION` ayarlıysa çalıştırır
- Genel paket registry yok (yerel `paketler/` + arşiv kurulumu)
- SemVer aralıkları / lockfile yok (yalnız kesin sürüm)
- HTTPS / sertifika kolaylık katmanı yok (HTTP Kestrel yeterli)
- Alan `ref`/`çıktı` ve async+ByRef US1900 ile daraltılmıştır

Tam liste: `docs/release-notes/v0.2.0.md` ve `docs/compiler-parity.md`.

## Örnekler

`samples/` altında Merhaba, HesapMakinesi, KontrolAkışı, Koleksiyonlar, Modeller, OOP, Async, Eşİşlem, Json, Dosya, Testler, WebApi, VeriErişimi, BackendAcceptance (Memory DB + Kestrel API).

## Test

```text
dotnet test Us.slnx
```
