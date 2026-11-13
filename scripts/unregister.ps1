#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the add-in registration from SolidWorks and COM. Run from an
    Administrator PowerShell. See register.ps1 for the execution policy note.
.EXAMPLE
    .\scripts\unregister.ps1
    .\scripts\unregister.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root "src\BajaAddIn\bin\$Configuration\net48\BajaAddIn.dll"
if (-not (Test-Path $dll)) {
    throw "$dll not found. Unregister with the same configuration you registered."
}

$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
& $regasm /unregister /nologo $dll
if ($LASTEXITCODE -ne 0) {
    throw "RegAsm failed with exit code $LASTEXITCODE."
}
Write-Host "Unregistered $dll."
