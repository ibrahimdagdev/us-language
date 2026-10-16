# Testable helpers for US Windows user-level install/uninstall.
# Dot-source from install.ps1 / uninstall.ps1 / tests. Do not run directly.

Set-StrictMode -Version Latest

function Get-UsInstallRoot {
    return (Join-Path $env:LOCALAPPDATA "US")
}

function Get-UsInstallBin {
    return (Join-Path (Get-UsInstallRoot) "bin")
}

function Normalize-UsPathEntry {
    param(
        [AllowNull()]
        [string] $PathEntry
    )

    if ([string]::IsNullOrWhiteSpace($PathEntry)) {
        return $null
    }

    $trimmed = $PathEntry.Trim().TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    try {
        return [System.IO.Path]::GetFullPath($trimmed)
    }
    catch {
        return $trimmed
    }
}

function Test-UsPathEntryEquals {
    param(
        [AllowNull()]
        [string] $Left,
        [AllowNull()]
        [string] $Right
    )

    $a = Normalize-UsPathEntry -PathEntry $Left
    $b = Normalize-UsPathEntry -PathEntry $Right
    if ($null -eq $a -or $null -eq $b) {
        return $false
    }

    return $a.Equals($b, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-UsUserPathEntries {
    $raw = [Environment]::GetEnvironmentVariable("Path", "User")
    if ([string]::IsNullOrEmpty($raw)) {
        return @()
    }

    return @(
        $raw.Split(';', [System.StringSplitOptions]::None) |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Set-UsUserPathEntries {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]] $Entries
    )

    $value = if ($Entries.Count -eq 0) { "" } else { ($Entries -join ";") }
    [Environment]::SetEnvironmentVariable("Path", $value, "User")
}

function Add-UsBinToUserPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BinPath
    )

    $entries = @(Get-UsUserPathEntries)
    foreach ($entry in $entries) {
        if (Test-UsPathEntryEquals -Left $entry -Right $BinPath) {
            return $false
        }
    }

    $entries += $BinPath
    Set-UsUserPathEntries -Entries $entries
    return $true
}

function Remove-UsBinFromUserPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BinPath
    )

    $entries = @(Get-UsUserPathEntries)
    $kept = @(
        $entries | Where-Object { -not (Test-UsPathEntryEquals -Left $_ -Right $BinPath) }
    )

    if ($kept.Count -eq $entries.Count) {
        return $false
    }

    Set-UsUserPathEntries -Entries $kept
    return $true
}

function Update-UsProcessPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $BinPath
    )

    $parts = @()
    if (-not [string]::IsNullOrEmpty($env:Path)) {
        $parts = @(
            $env:Path.Split(';', [System.StringSplitOptions]::None) |
                ForEach-Object { $_.Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
    }

    $found = $false
    foreach ($part in $parts) {
        if (Test-UsPathEntryEquals -Left $part -Right $BinPath) {
            $found = $true
            break
        }
    }

    if (-not $found) {
        $parts = @($BinPath) + $parts
        $env:Path = ($parts -join ";")
    }
}

function Get-UsRuntimeFileNames {
    return @(
        "us.exe",
        "Us.Cli.dll",
        "Us.Cli.deps.json",
        "Us.Cli.runtimeconfig.json",
        "Us.Core.dll",
        "Us.Lexer.dll",
        "Us.Parser.dll",
        "Us.Semantics.dll",
        "Us.Compiler.dll",
        "Us.Interpreter.dll",
        "Us.Runtime.dll",
        "Us.Tooling.dll"
    )
}

function Get-UsVsCodeExtensionId {
    return "us-lang.us-language"
}

function Get-UsVsixFileName {
    return "us-language-0.2.0.vsix"
}

function Find-UsCodeCli {
    $fromPath = Get-Command "code" -ErrorAction SilentlyContinue
    if ($null -ne $fromPath -and $fromPath.Source) {
        return $fromPath.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Microsoft VS Code\bin\code.cmd"),
        (Join-Path $env:ProgramFiles "Microsoft VS Code\bin\code.cmd"),
        (Join-Path $programFilesX86 "Microsoft VS Code\bin\code.cmd")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Install-UsVsCodeExtension {
    param(
        [Parameter(Mandatory = $true)]
        [string] $VsixPath
    )

    if (-not (Test-Path -LiteralPath $VsixPath)) {
        return @{ Status = "MissingVsix"; Message = "VSIX bulunamadi: $VsixPath"; CodeCli = $null }
    }

    $codeCli = Find-UsCodeCli
    if ([string]::IsNullOrWhiteSpace($codeCli)) {
        return @{
            Status  = "CodeMissing"
            Message = "US CLI kuruldu. VS Code bulunamadigi icin extension otomatik kurulmadi."
            CodeCli = $null
        }
    }

    $args = @("--install-extension", $VsixPath, "--force")
    $p = Start-Process -FilePath $codeCli -ArgumentList $args -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        return @{
            Status  = "InstallFailed"
            Message = "VS Code extension kurulum basarisiz (exit $($p.ExitCode))."
            CodeCli = $codeCli
        }
    }

    return @{
        Status  = "Installed"
        Message = "US Programming Language VS Code extension kuruldu."
        CodeCli = $codeCli
    }
}

function Uninstall-UsVsCodeExtension {
    $codeCli = Find-UsCodeCli
    if ([string]::IsNullOrWhiteSpace($codeCli)) {
        return @{ Status = "CodeMissing"; Message = "VS Code bulunamadi; extension kaldirma atlandi." }
    }

    $extensionId = Get-UsVsCodeExtensionId
    $listed = & $codeCli --list-extensions 2>$null
    if ($LASTEXITCODE -ne 0) {
        return @{ Status = "ListFailed"; Message = "VS Code extension listesi alinamadi; kaldirma atlandi." }
    }

    $installed = @($listed | Where-Object { $_ -eq $extensionId })
    if ($installed.Count -eq 0) {
        return @{ Status = "NotInstalled"; Message = "US VS Code extension kurulu degildi." }
    }

    & $codeCli --uninstall-extension $extensionId 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        return @{ Status = "UninstallFailed"; Message = "US VS Code extension kaldirilamadi (exit $LASTEXITCODE)." }
    }

    return @{ Status = "Uninstalled"; Message = "US VS Code extension kaldirildi." }
}
