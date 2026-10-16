#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes Us.Cli for win-x64, packages the VS Code VSIX, and builds US-v0.2.0-win-x64.zip.
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "src\Us.Cli\Us.Cli.csproj"
$packaging = Join-Path $repoRoot "packaging\windows"
$outRoot = Join-Path $repoRoot "artifacts\win-x64"
$publishDir = Join-Path $outRoot "publish"
$stageDir = Join-Path $outRoot "stage"
$zipPath = Join-Path $outRoot "US-v0.2.0-win-x64.zip"
$vsixPath = Join-Path $repoRoot "artifacts\vscode\us-language-0.2.0.vsix"

& (Join-Path $PSScriptRoot "package-vscode.ps1")
if ($LASTEXITCODE -ne 0 -and -not (Test-Path -LiteralPath $vsixPath)) {
    throw "VS Code packaging failed and VSIX is missing."
}
if (-not (Test-Path -LiteralPath $vsixPath)) {
    throw "Expected VSIX missing: $vsixPath"
}

if (Test-Path -LiteralPath $outRoot) {
    Remove-Item -LiteralPath $outRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDir, $stageDir -Force | Out-Null

dotnet publish $cliProject `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $publishDir `
    /p:DebugType=None `
    /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$usExe = Join-Path $publishDir "us.exe"
$legacyExe = Join-Path $publishDir "Us.Cli.exe"
if (-not (Test-Path -LiteralPath $usExe)) {
    throw "Expected public host missing: $usExe"
}
if (Test-Path -LiteralPath $legacyExe) {
    throw "Legacy public host should not remain after publish: $legacyExe"
}

# Stage full publish output (CLI + Hosting + SQL provider + SqlClient deps).
Get-ChildItem -LiteralPath $publishDir -Force | ForEach-Object {
    $dest = Join-Path $stageDir $_.Name
    if ($_.PSIsContainer) {
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }
}

$required = @(
    "us.exe",
    "Us.Cli.dll",
    "Us.Compiler.dll",
    "Us.Runtime.dll",
    "Us.Hosting.dll",
    "Us.Data.SqlServer.dll",
    "Microsoft.Data.SqlClient.dll"
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $stageDir $name))) {
        throw "Publish/stage missing required artifact: $name"
    }
}

foreach ($name in @("install.ps1", "uninstall.ps1", "install.cmd", "UsInstall.Helpers.ps1", "README.txt")) {
    Copy-Item -LiteralPath (Join-Path $packaging $name) -Destination (Join-Path $stageDir $name) -Force
}

Copy-Item -LiteralPath $vsixPath -Destination (Join-Path $stageDir "us-language-0.2.0.vsix") -Force

Get-ChildItem -LiteralPath $stageDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -Force

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $zipPath).Length

Write-Host "ZIP: $zipPath"
Write-Host "SIZE: $size"
Write-Host "SHA256: $hash"
Write-Host "ROOT has us.exe: $(Test-Path (Join-Path $stageDir 'us.exe'))"
Write-Host "ROOT has Us.Hosting.dll: $(Test-Path (Join-Path $stageDir 'Us.Hosting.dll'))"
Write-Host "ROOT has Us.Data.SqlServer.dll: $(Test-Path (Join-Path $stageDir 'Us.Data.SqlServer.dll'))"
Write-Host "ROOT has VSIX: $(Test-Path (Join-Path $stageDir 'us-language-0.2.0.vsix'))"
