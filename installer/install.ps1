<#
.SYNOPSIS
    Installs the LBR CAD Tools SolidWorks add-in for everyone on this computer.
.DESCRIPTION
    Started by Install.cmd. Asks for administrator rights, copies the add-in
    to "C:\Program Files\LBR CAD Tools", and registers it with SolidWorks.
    Settings in %LOCALAPPDATA%\LBR CAD Tools are kept.
#>
$ErrorActionPreference = 'Stop'

# Runs 64-bit RegAsm and returns its exit code. RegAsm always prints warning
# RA0000 (unsigned assembly) on stderr; with ErrorActionPreference=Stop,
# PowerShell 5.1 would turn that line into a terminating error, so run it
# with Continue and judge only by the exit code.
function Invoke-RegAsm([string[]]$arguments) {
    $regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $regasm @arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        $lines | Where-Object { $_ -notmatch 'RA0000' } | ForEach-Object { Write-Host "  $_" }
    }
    return $code
}
function Finish([int]$code) {
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
    exit $code
}

# Re-run elevated: registering a SolidWorks add-in writes to HKLM.
$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    }
    catch {
        Write-Host 'Administrator rights are needed to install. Run Install.cmd again and click Yes.' -ForegroundColor Red
        Finish 1
    }
    exit 0
}

try {
    Write-Host 'LBR CAD Tools installer' -ForegroundColor Cyan
    Write-Host ''

    $source = Join-Path $PSScriptRoot 'bin'
    $dllName = 'BajaAddIn.dll'
    if (-not (Test-Path (Join-Path $source $dllName))) {
        throw "Cannot find bin\$dllName next to this script. Unzip the whole download first, then run Install.cmd from the unzipped folder."
    }

    if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
        Write-Host 'SolidWorks is open. Save your work, close SolidWorks, then run Install.cmd again.' -ForegroundColor Yellow
        Finish 1
    }

    $regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
    if (-not (Test-Path $regasm)) {
        throw "64-bit RegAsm not found at $regasm. Install .NET Framework 4.8 (Windows Update) and try again."
    }

    # Files from a downloaded zip carry an "from the internet" mark that can stop .NET loading them.
    Get-ChildItem $PSScriptRoot -Recurse -File | Unblock-File

    $target = Join-Path $env:ProgramFiles 'LBR CAD Tools'
    $installed = Join-Path $target $dllName
    if (Test-Path $installed) {
        Write-Host 'Removing the previous version...'
        Invoke-RegAsm @('/unregister', '/nologo', $installed) | Out-Null
        Remove-Item (Join-Path $target '*') -Recurse -Force
    }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $source '*') $target -Recurse -Force
    Write-Host "Copied to $target"

    Write-Host 'Registering with SolidWorks...'
    $code = Invoke-RegAsm @('/codebase', '/nologo', $installed)
    if ($code -ne 0) {
        throw "Registration failed (RegAsm exit code $code)."
    }

    # Compare with the SolidWorks version the release was built against.
    $info = Join-Path $target 'build-info.txt'
    $builtYear = $null
    if (Test-Path $info) {
        $line = Get-Content $info | Where-Object { $_ -match '^SolidWorks year:\s*(\d{4})' } | Select-Object -First 1
        if ($line -match '(\d{4})') { $builtYear = [int]$Matches[1] }
    }
    $years = @(Get-ChildItem 'HKLM:\SOFTWARE\SolidWorks' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^SOLIDWORKS (\d{4})$' } |
        ForEach-Object { [int]($_.PSChildName -replace '\D', '') })
    if ($years.Count -eq 0) {
        Write-Host 'Note: no SolidWorks installation was found in the registry.' -ForegroundColor Yellow
    }
    elseif ($builtYear -and ($years | Measure-Object -Maximum).Maximum -lt $builtYear) {
        Write-Host "Note: this release was built for SolidWorks $builtYear and you have $(($years | Measure-Object -Maximum).Maximum)." -ForegroundColor Yellow
        Write-Host '      Most tools should work; if one fails, report it with the log file below.' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host 'Installed.' -ForegroundColor Green
    Write-Host 'Start SolidWorks and open a part, assembly, or drawing: the LBR CAD Tools tab appears at the top.'
    Write-Host 'If it does not, use Tools > Add-Ins and tick LBR CAD Tools (both boxes).'
    Write-Host "Log file if something goes wrong: $env:LOCALAPPDATA\LBR CAD Tools\BajaAddIn.log"
    Finish 0
}
catch {
    Write-Host ''
    Write-Host "Install failed: $($_.Exception.Message)" -ForegroundColor Red
    Finish 1
}
