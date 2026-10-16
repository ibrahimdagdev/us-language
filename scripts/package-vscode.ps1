#Requires -Version 5.1
<#
.SYNOPSIS
  Bundles and packages the US VS Code extension into us-language-0.2.0.vsix.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$extDir = Join-Path $repoRoot "editors\vscode"
$outDir = Join-Path $repoRoot "artifacts\vscode"
$vsixPath = Join-Path $outDir "us-language-0.2.0.vsix"

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required to package the VS Code extension."
}

New-Item -ItemType Directory -Path $outDir -Force | Out-Null
if (Test-Path -LiteralPath $vsixPath) {
    Remove-Item -LiteralPath $vsixPath -Force
}

Push-Location $extDir
try {
    npm install --no-fund --no-audit
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE"
    }

    npm run package
    if ($LASTEXITCODE -ne 0) {
        throw "npm run package failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $vsixPath)) {
    throw "Expected VSIX missing: $vsixPath"
}

# Basic package.json/version sanity inside VSIX (zip)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($vsixPath)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq "extension/package.json" } | Select-Object -First 1
    if ($null -eq $entry) {
        throw "VSIX missing extension/package.json"
    }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    $pkgText = $reader.ReadToEnd()
    $reader.Close()
    if ($pkgText -notmatch '"version"\s*:\s*"0\.2\.0"') {
        throw "VSIX package.json version is not 0.2.0"
    }
    if ($pkgText -notmatch '"displayName"\s*:\s*"US Programming Language"') {
        throw "VSIX displayName mismatch"
    }
    $hasMain = @($zip.Entries | Where-Object { $_.FullName -eq "extension/dist/extension.js" }).Count -gt 0
    $hasGrammar = @($zip.Entries | Where-Object { $_.FullName -eq "extension/syntaxes/us.tmLanguage.json" }).Count -gt 0
    if (-not $hasMain) { throw "VSIX missing dist/extension.js" }
    if (-not $hasGrammar) { throw "VSIX missing TextMate grammar" }
}
finally {
    $zip.Dispose()
}

$hash = (Get-FileHash -LiteralPath $vsixPath -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $vsixPath).Length
Write-Host "VSIX: $vsixPath"
Write-Host "SIZE: $size"
Write-Host "SHA256: $hash"
