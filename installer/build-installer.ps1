# Builds installer\output\Almatter-Setup-<version>.exe from scratch:
#   1. the Rust core in release mode (almatter_ffi.dll),
#   2. the app compiled with Native AOT for win-x64 into installer\publish
#      (one native Almatter.exe: no .NET runtime to install on the user's PC),
#   3. the Inno Setup script, with the version taken from Almatter.App.csproj.
#
# Usage (from any folder):  powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1

$ErrorActionPreference = 'Stop'

$installerDir = $PSScriptRoot
$repo = Split-Path $installerDir -Parent
$appProject = Join-Path $repo 'app\Almatter.App\Almatter.App.csproj'
$publishDir = Join-Path $installerDir 'publish'

function Find-Tool([string]$name, [string[]]$candidates) {
    $onPath = Get-Command $name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "$name not found. Install it or add it to PATH."
}

$cargo = Find-Tool 'cargo' @("$env:USERPROFILE\.cargo\bin\cargo.exe")
$iscc = Find-Tool 'ISCC' @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")

$version = ([xml](Get-Content $appProject -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $appProject" }
Write-Host "== Almatter $version"

Write-Host '== Rust core (release)'
Push-Location (Join-Path $repo 'core')
try {
    & $cargo build --release -p almatter-ffi
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed ($LASTEXITCODE)" }
}
finally {
    Pop-Location
}

Write-Host '== Publishing the app (Native AOT)'
# The AOT linker step finds the Visual Studio C++ tools through vswhere.exe,
# looked up on PATH. The Visual Studio Installer puts it in its own folder
# without adding that folder to PATH, so a PC with only the Build Tools fails
# at the very last step with a garbled link.exe command line.
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command 'vswhere.exe' -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $vsInstaller 'vswhere.exe'))) {
    $env:PATH = "$vsInstaller;$env:PATH"
}
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
# PublishAot comes from the csproj. Warnings are listed rather than
# tolerated silently: under AOT, a trim or AOT warning is a feature that may
# fail at run time, not a style remark.
# MSBuild prints warnings and errors on standard output, so Tee-Object sees
# them all; no 2>&1, which Windows PowerShell 5.1 turns into a terminating
# error under $ErrorActionPreference = 'Stop'.
& dotnet publish $appProject -c Release -r win-x64 -o $publishDir | Tee-Object -Variable publishLog
$publishExit = $LASTEXITCODE
if ($publishExit -ne 0) { throw "dotnet publish failed ($publishExit)" }
$aotWarnings = $publishLog | Select-String -Pattern 'warning (IL\d{4}|CS\d{4}|MSB\d{4})|warning :' | Select-Object -ExpandProperty Line -Unique
if ($aotWarnings) { throw "dotnet publish reported warnings; fix them before shipping:`n$($aotWarnings -join "`n")" }

# The csproj only copies the Rust DLL when it exists, so a missing one would
# otherwise produce an installer for an app that cannot start.
$ffi = Join-Path $publishDir 'almatter_ffi.dll'
if (-not (Test-Path $ffi)) { throw "almatter_ffi.dll is missing from $publishDir" }

# A leftover from the framework-dependent days would mean a stale build.
if (Test-Path (Join-Path $publishDir 'Almatter.dll')) { throw "Almatter.dll in ${publishDir}: the publish was not Native AOT" }

Write-Host '== Compiling the installer'
& $iscc "/DAppVersion=$version" (Join-Path $installerDir 'Almatter.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $installerDir "output\Almatter-Setup-$version.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "== Done: $setup ($sizeMb MB)"
