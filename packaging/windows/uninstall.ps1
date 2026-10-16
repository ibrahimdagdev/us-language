#Requires -Version 5.1
<#
.SYNOPSIS
  Removes US CLI user install from %LOCALAPPDATA%\US, the matching USER PATH entry,
  and the US VS Code extension when present.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "UsInstall.Helpers.ps1")

$bin = Get-UsInstallBin
$root = Get-UsInstallRoot

$vsixResult = Uninstall-UsVsCodeExtension
Write-Host $vsixResult.Message

$removedPath = Remove-UsBinFromUserPath -BinPath $bin

if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
    Write-Host "Kurulum dizini kaldirildi: $root"
}
else {
    Write-Host "Kurulum dizini zaten yok: $root"
}

if ($removedPath) {
    Write-Host "USER PATH girdisi kaldirildi: $bin"
}
else {
    Write-Host "USER PATH'te US bin girdisi yoktu: $bin"
}

Write-Host ""
Write-Host "Bu ZIP/klasor silinmedi: $scriptDir"
Write-Host "Yeni bir terminalde 'us' komutu artik bu kurulumdan gelmemeli."
