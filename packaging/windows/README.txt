US Programming Language v0.2.0
Turkce dusun. Kodla.

GEREKSINIMLER
  - Windows x64
  - .NET 10 runtime (framework-dependent paket)
  - VS Code (opsiyonel; extension otomatik kurulur)

KURULUM (EN KOLAY)

1. ZIP dosyasini bir klasore cikarin.
2. install.cmd dosyasina cift tiklayin.
3. Yeni bir terminal veya VS Code acin.
4. Kontrol edin:

    us --version

Beklenen:

    0.2.0

Kurulum konumu (yonetici gerekmez):

    %LOCALAPPDATA%\US\bin

VS Code kuruluysa installer US extension'ini otomatik kurar:

    US Programming Language (us-lang.us-language) 0.2.0

PowerShell ile kurulum:

    powershell -ExecutionPolicy Bypass -File .\install.ps1

Manuel VS Code extension kurulumu:

    code --install-extension us-language-0.2.0.vsix

ORNEK

    us yeni konsol Merhaba
    cd Merhaba
    us calistir

Beklenen:

    Merhaba US!

Derleme:

    us derle
    dotnet cikti\UsProgram.dll

Beklenen:

    Merhaba US!

Web/API ornegi (Memory DB + Kestrel):

    us derle samples/BackendAcceptance
    set US_URLS=http://127.0.0.1:5080
    dotnet samples\BackendAcceptance\cikti\UsProgram.dll

DIGER KOMUTLAR

    us test
    us bicimlendir
    us paket ekle <ad> [surum]
    us paket kur <arsiv.zip>
    us --version
    us dil-sunucusu

KALDIRMA

    uninstall.ps1

veya:

    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1

NOTLAR

  - install.ps1 USER PATH'e ekler; Machine PATH'e dokunmaz.
  - Acik terminaller otomatik guncellenmez; yeni terminal acin.
  - Public komut: us
  - Managed assembly: Us.Cli.dll
  - Us.Hosting.dll ve Us.Data.SqlServer.dll paket icerisinde bulunur.
  - SQL Server canli dogrulama ortama baglidir (US_TEST_SQLSERVER_CONNECTION).
  - VS Code bulunamazsa CLI kurulumu yine basarilidir.
