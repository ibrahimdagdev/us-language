#Requires -Version 5.1
<#
.SYNOPSIS
  Installs US CLI for the current user into %LOCALAPPDATA%\US\bin, updates USER PATH,
  and optionally installs the US VS Code extension when VSIX + code CLI are available.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "UsInstall.Helpers.ps1")

$bin = Get-UsInstallBin
$root = Get-UsInstallRoot
$required = Get-UsRuntimeFileNames

foreach ($name in $required) {
    $source = Join-Path $scriptDir $name
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Kurulum paketi eksik dosya: $name (beklenen konum: $scriptDir)"
    }
}

New-Item -ItemType Directory -Path $bin -Force | Out-Null

foreach ($name in $required) {
    Copy-Item -LiteralPath (Join-Path $scriptDir $name) -Destination (Join-Path $bin $name) -Force
}

foreach ($optional in @(
        "README.txt",
        "install.ps1",
        "uninstall.ps1",
        "install.cmd",
        "UsInstall.Helpers.ps1",
        (Get-UsVsixFileName)
    )) {
    $source = Join-Path $scriptDir $optional
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination (Join-Path $bin $optional) -Force
    }
}

$added = Add-UsBinToUserPath -BinPath $bin
Update-UsProcessPath -BinPath $bin

$usExe = Join-Path $bin "us.exe"
Write-Host ""
Write-Host "US Programming Language v0.2.0"
Write-Host "US CLI kurulumu tamamlandi."
Write-Host "Komut: us"
Write-Host "Konum: $bin"
Write-Host "Executable: $usExe"
if ($added) {
    Write-Host "USER PATH'e eklendi: $bin"
}
else {
    Write-Host "USER PATH zaten iceriyordu: $bin"
}

$vsixPath = Join-Path $scriptDir (Get-UsVsixFileName)
$vsixResult = Install-UsVsCodeExtension -VsixPath $vsixPath
Write-Host $vsixResult.Message
if ($vsixResult.Status -eq "MissingVsix") {
    Write-Host "Manuel kurulum icin VSIX paket icinde bulunmali: $(Get-UsVsixFileName)"
}
elseif ($vsixResult.Status -eq "CodeMissing") {
    Write-Host "Manuel: code --install-extension $(Get-UsVsixFileName)"
}

Write-Host ""
Write-Host "Bu betik yalnizca USER PATH'i gunceller (Machine PATH'e dokunmaz)."
Write-Host "Mevcut acik terminal otomatik guncellenmez."
Write-Host "Yeni bir terminal veya VS Code acarak sunu calistirin:"
Write-Host "  us --version"
Write-Host ""
Write-Host "Kurulum kok: $root"
