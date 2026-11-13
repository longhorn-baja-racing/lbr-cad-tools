<#
.SYNOPSIS
    Builds a release zip of the add-in for team members to install.
.DESCRIPTION
    Builds src\BajaAddIn in Release, then stages dist\LBR-CAD-Tools-<version>
    with the add-in, its SolidWorks interop assemblies (from the local
    SolidWorks install's api\redist folder, the exact versions it was built
    against), the installer scripts, and a build-info.txt, and zips it.
    Needs Windows with SolidWorks installed. The version comes from
    Directory.Build.props.
.EXAMPLE
    .\scripts\package.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

Write-Host "Building LBR CAD Tools $version (Release)..."
& dotnet build (Join-Path $root 'src\BajaAddIn\BajaAddIn.csproj') -c Release --no-incremental --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }

$out = Join-Path $root 'src\BajaAddIn\bin\Release\net48'
$files = @(
    'BajaAddIn.dll',
    'BajaAddIn.Core.dll',
    'SolidWorks.Interop.sldworks.dll',
    'SolidWorks.Interop.swconst.dll',
    'SolidWorks.Interop.swpublished.dll'
)

$name = "LBR-CAD-Tools-$version"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'bin') | Out-Null

foreach ($f in $files) {
    $path = Join-Path $out $f
    if (-not (Test-Path $path)) { throw "Missing build output: $path" }
    Copy-Item $path (Join-Path $stage 'bin')
}

# Installer scripts with CRLF line endings (cmd.exe and Notepad users expect them).
foreach ($f in Get-ChildItem (Join-Path $root 'installer') -File) {
    $text = [IO.File]::ReadAllText($f.FullName) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText((Join-Path $stage $f.Name), $text, (New-Object Text.UTF8Encoding($false)))
}

# Which SolidWorks the interop assemblies came from; install.ps1 compares it with the installed one.
$interop = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $out 'SolidWorks.Interop.sldworks.dll')).Version
$year = 1992 + $interop.Major  # 34 -> 2026
$git = (Get-Command git -ErrorAction SilentlyContinue).Source
if (-not $git -and (Test-Path "$env:ProgramFiles\Git\cmd\git.exe")) { $git = "$env:ProgramFiles\Git\cmd\git.exe" }
$commit = if ($git) { (& $git -C $root rev-parse --short HEAD).Trim() } else { 'unknown' }
@(
    "LBR CAD Tools $version",
    "Commit: $commit",
    "SolidWorks year: $year",
    "SolidWorks interop: $interop",
    "Built: $(Get-Date -Format 'yyyy-MM-dd')"
) -join "`r`n" | Set-Content -Encoding ascii (Join-Path $stage 'bin\build-info.txt')

$zip = Join-Path $dist "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Wrote $zip"
Get-ChildItem $stage -Recurse -File | ForEach-Object { '  ' + $_.FullName.Substring($stage.Length + 1) }
