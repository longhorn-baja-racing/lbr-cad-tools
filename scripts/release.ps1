<#
.SYNOPSIS
    Builds LBR-CAD-Tools-Setup.exe and publishes it as a GitHub Release.
.DESCRIPTION
    One command for a release:
      1. Checks the working tree is clean, on main, and in sync with origin,
         and that the tag v<version> does not exist yet (version comes from
         Directory.Build.props; bump it in a PR first).
      2. Clean Release build of src/Setup (which embeds the add-in and the
         SolidWorks interop assemblies it was built against), then
         tools/ApiCheck: fails if the add-in calls any SolidWorks API newer
         than SolidWorks 2025, the oldest version the team runs (issue #14).
      3. Writes release notes: install steps, then the commits since the last
         tag (or -NotesFile for a hand-written summary).
      4. Tags v<version>, pushes the tag, and creates the GitHub Release with
         the Setup.exe attached (gh CLI).
    GitHub Actions cannot do this: the build needs the SolidWorks interop
    assemblies, which only exist on a machine with SolidWorks installed.

    -DryRun stops after step 3 and leaves the exe and notes in dist/.
.EXAMPLE
    .\scripts\release.ps1 -DryRun
    .\scripts\release.ps1
    .\scripts\release.ps1 -NotesFile my-notes.md
#>
param(
    [switch]$DryRun,
    [string]$NotesFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Find-Tool([string]$name, [string]$fallback) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    if ($fallback -and (Test-Path $fallback)) { return $fallback }
    throw "$name not found. Install it or add it to PATH."
}

# Runs a native tool and returns its output; stderr does not count as failure, the exit code does.
function Run([string]$exe, [string[]]$arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $exe @arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($code -ne 0) {
        throw "$([IO.Path]::GetFileName($exe)) $($arguments -join ' ') failed (exit code $code):`n$($out -join "`n")"
    }
    return $out
}

# Oldest SolidWorks the team runs (issue #14). Keep in sync with Installer.MinimumYear.
$MinSolidWorks = 2025

$git = Find-Tool 'git' "$env:ProgramFiles\Git\cmd\git.exe"
$gh = Find-Tool 'gh' "$env:ProgramFiles\GitHub CLI\gh.exe"
$dotnet = Find-Tool 'dotnet' $null

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = "$($props.Project.PropertyGroup.Version)".Trim()
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
$tag = "v$version"
Write-Host "LBR CAD Tools $version" -ForegroundColor Cyan

# ---- 1. Checks ----
Push-Location $root
try {
    if (-not $DryRun) {
        if (Run $git @('status', '--porcelain')) { throw 'The working tree has changes. Commit or stash them first.' }
        $branch = (Run $git @('rev-parse', '--abbrev-ref', 'HEAD')) -join ''
        if ($branch -ne 'main') { throw "Releases are made from main (current branch: $branch)." }
        Run $git @('fetch', '--quiet', '--tags', 'origin') | Out-Null
        $head = (Run $git @('rev-parse', 'HEAD')) -join ''
        $remote = (Run $git @('rev-parse', 'origin/main')) -join ''
        if ($head -ne $remote) { throw 'main is not in sync with origin/main. Pull or push first.' }
        if (Run $git @('tag', '--list', $tag)) { throw "Tag $tag already exists. Bump <Version> in Directory.Build.props (in a PR) first." }
    }

    # ---- 2. Build ----
    Write-Host 'Building Setup (Release, clean)...'
    Run $dotnet @('build', (Join-Path $root 'src\Setup\Setup.csproj'), '-c', 'Release', '--no-incremental', '--nologo', '-v', 'quiet') | Out-Null
    $built = Join-Path $root 'src\Setup\bin\Release\net48\LBR-CAD-Tools-Setup.exe'
    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $exe = Join-Path $dist "LBR-CAD-Tools-Setup-$version.exe"
    Copy-Item $built $exe -Force
    $fileVersion = (Get-Item $exe).VersionInfo.FileVersion
    if (-not $fileVersion.StartsWith($version)) { throw "Built exe has version $fileVersion, expected $version." }
    Write-Host "  $exe ($([math]::Round((Get-Item $exe).Length / 1MB, 1)) MB)"

    Write-Host "Checking SolidWorks API use against SolidWorks $MinSolidWorks..."
    $addInDll = Join-Path $root 'src\BajaAddIn\bin\Release\net48\BajaAddIn.dll'
    Run $dotnet @('run', '--project', (Join-Path $root 'tools\ApiCheck\ApiCheck.csproj'), '-c', 'Release', '--', '--max-year', "$MinSolidWorks", $addInDll) |
        ForEach-Object { Write-Host "  $_" }

    # ---- 3. Notes ----
    $previous = $null
    try { $previous = (Run $git @('describe', '--tags', '--abbrev=0')) -join '' } catch { }
    $notes = New-Object System.Collections.Generic.List[string]
    $notes.Add('SolidWorks add-in for Longhorn Baja Racing, The University of Texas at Austin.')
    $notes.Add('')
    $notes.Add('## Install')
    $notes.Add('1. Close SolidWorks.')
    $notes.Add("2. Download **LBR-CAD-Tools-Setup-$version.exe** below and run it. Click Yes when Windows asks for permission. If Windows shows ""Windows protected your PC"", click **More info**, then **Run anyway**.")
    $notes.Add('3. Click **Install** (or **Update**), then start SolidWorks. The **LBR CAD Tools** tab appears in parts, assemblies, and drawings.')
    $notes.Add('')
    $notes.Add('To remove it: Windows Settings > Apps > LBR CAD Tools > Uninstall. Settings in %LOCALAPPDATA%\LBR CAD Tools are kept.')
    $notes.Add('')
    if ($NotesFile) {
        foreach ($line in Get-Content $NotesFile) { $notes.Add($line) }
        $notes.Add('')
    }
    if ($previous) {
        $notes.Add("## Changes since $previous")
        foreach ($line in Run $git @('log', '--no-merges', '--format=- %s', "$previous..HEAD")) { $notes.Add($line) }
        $notes.Add('')
    }
    $notes.Add("Requires SolidWorks $MinSolidWorks or newer. Commit $((Run $git @('rev-parse', '--short', 'HEAD')) -join '').")
    $notesPath = Join-Path $dist "release-notes-$version.md"
    $text = ($notes -join "`n") + "`n"
    if ($text -match '[^\x09\x0A\x0D\x20-\x7E]') { throw 'Release notes contain non-ASCII characters.' }
    [IO.File]::WriteAllText($notesPath, $text)
    Write-Host "  $notesPath"

    if ($DryRun) {
        Write-Host 'Dry run: nothing tagged or published.' -ForegroundColor Yellow
        return
    }

    # ---- 4. Publish ----
    Write-Host "Tagging $tag and publishing..."
    Run $git @('tag', '-a', $tag, '-m', "LBR CAD Tools $version") | Out-Null
    Run $git @('push', 'origin', $tag) | Out-Null
    Run $gh @('release', 'create', $tag, $exe, '--title', "LBR CAD Tools $version", '--notes-file', $notesPath) | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Released.' -ForegroundColor Green
}
finally {
    Pop-Location
}
