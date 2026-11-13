#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Registers the built add-in with SolidWorks.
.DESCRIPTION
    Runs 64-bit RegAsm /codebase on the BajaAddIn.dll build output. Build first
    with "dotnet build" (Debug) or "dotnet build -c Release". Run from an
    Administrator PowerShell; close SolidWorks before rebuilding, because it
    locks the DLL while loaded.

    If Windows says "running scripts is disabled on this system", allow local
    scripts once per account with:
        Set-ExecutionPolicy -Scope CurrentUser RemoteSigned

    RegAsm warns (RA0000) that the assembly is unsigned. That warning is
    expected and harmless for a team-internal add-in.
.EXAMPLE
    .\scripts\register.ps1
    .\scripts\register.ps1 -Configuration Release
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root "src\BajaAddIn\bin\$Configuration\net48\BajaAddIn.dll"
if (-not (Test-Path $dll)) {
    throw "Add-in not built: $dll not found. Run 'dotnet build -c $Configuration' first."
}

$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
if (-not (Test-Path $regasm)) {
    throw "64-bit RegAsm not found at $regasm. Is .NET Framework 4.8 installed?"
}

& $regasm /codebase /nologo $dll
if ($LASTEXITCODE -ne 0) {
    throw "RegAsm failed with exit code $LASTEXITCODE."
}
Write-Host "Registered $dll. Start SolidWorks and look for the 'LBR CAD Tools' tab."
