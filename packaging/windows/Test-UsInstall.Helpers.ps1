#Requires -Version 5.1
# Self-check for UsInstall.Helpers.ps1. Exit 0 and print OK on success.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "UsInstall.Helpers.ps1")

if (-not (Test-UsPathEntryEquals -Left "C:\Foo" -Right "C:\Foo\")) {
    throw "slash normalize failed"
}
if (-not (Test-UsPathEntryEquals -Left "C:\Foo" -Right "c:\foo")) {
    throw "case normalize failed"
}

$marker = "US-PATH-TEST-" + [guid]::NewGuid().ToString("N")
$fakeBin = Join-Path $env:TEMP $marker
$other = Join-Path $env:TEMP ("OTHER-" + $marker)
New-Item -ItemType Directory -Path $fakeBin, $other -Force | Out-Null

$original = [Environment]::GetEnvironmentVariable("Path", "User")
try {
    $null = Add-UsBinToUserPath -BinPath $other

    $first = Add-UsBinToUserPath -BinPath $fakeBin
    $second = Add-UsBinToUserPath -BinPath $fakeBin
    if ($first -ne $true) { throw "first add should return true" }
    if ($second -ne $false) { throw "second add should be idempotent false" }

    $afterAdd = @(Get-UsUserPathEntries)
    $count = @($afterAdd | Where-Object { Test-UsPathEntryEquals -Left $_ -Right $fakeBin }).Count
    if ($count -ne 1) { throw "expected one fake bin entry, got $count" }
    if (-not ($afterAdd | Where-Object { Test-UsPathEntryEquals -Left $_ -Right $other })) {
        throw "other PATH entry lost after add"
    }

    $removed1 = Remove-UsBinFromUserPath -BinPath $fakeBin
    $removed2 = Remove-UsBinFromUserPath -BinPath $fakeBin
    if ($removed1 -ne $true) { throw "first remove should return true" }
    if ($removed2 -ne $false) { throw "second remove should be idempotent false" }

    $afterRemove = @(Get-UsUserPathEntries)
    if ($afterRemove | Where-Object { Test-UsPathEntryEquals -Left $_ -Right $fakeBin }) {
        throw "fake bin still present"
    }
    if (-not ($afterRemove | Where-Object { Test-UsPathEntryEquals -Left $_ -Right $other })) {
        throw "other PATH entry lost after remove"
    }

    Write-Output "OK"
}
finally {
    [Environment]::SetEnvironmentVariable("Path", $original, "User")
    Remove-Item -LiteralPath $fakeBin -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $other -Recurse -Force -ErrorAction SilentlyContinue
}
