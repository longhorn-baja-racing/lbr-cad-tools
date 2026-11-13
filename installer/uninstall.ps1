<#
.SYNOPSIS
    Removes the LBR CAD Tools SolidWorks add-in.
.DESCRIPTION
    Started by Uninstall.cmd. Asks for administrator rights, unregisters the
    add-in, and deletes "C:\Program Files\LBR CAD Tools". Settings in
    %LOCALAPPDATA%\LBR CAD Tools are left in place.
#>
$ErrorActionPreference = 'Stop'

function Finish([int]$code) {
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
    exit $code
}

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    }
    catch {
        Write-Host 'Administrator rights are needed to uninstall. Run Uninstall.cmd again and click Yes.' -ForegroundColor Red
        Finish 1
    }
    exit 0
}

try {
    if (Get-Process SLDWORKS -ErrorAction SilentlyContinue) {
        Write-Host 'SolidWorks is open. Close it, then run Uninstall.cmd again.' -ForegroundColor Yellow
        Finish 1
    }

    $target = Join-Path $env:ProgramFiles 'LBR CAD Tools'
    $installed = Join-Path $target 'BajaAddIn.dll'
    if (-not (Test-Path $installed)) {
        Write-Host "LBR CAD Tools is not installed in $target." -ForegroundColor Yellow
        Finish 0
    }

    $regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
    & $regasm /unregister /nologo $installed | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Unregistering failed (RegAsm exit code $LASTEXITCODE)."
    }
    Remove-Item $target -Recurse -Force

    Write-Host 'LBR CAD Tools was removed.' -ForegroundColor Green
    Write-Host "Your settings are still in $env:LOCALAPPDATA\LBR CAD Tools (delete that folder to remove them too)."
    Finish 0
}
catch {
    Write-Host ''
    Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
    Finish 1
}
