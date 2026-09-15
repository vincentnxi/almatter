# Builds installer\output\Almatter-Setup-<version>.exe from scratch:
#   1. the Rust core in release mode (almatter_ffi.dll),
#   2. the app published framework-dependent for win-x64 into installer\publish,
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

Write-Host '== Publishing the app'
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
& dotnet publish $appProject -c Release -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# The csproj only copies the Rust DLL when it exists, so a missing one would
# otherwise produce an installer for an app that cannot start.
$ffi = Join-Path $publishDir 'almatter_ffi.dll'
if (-not (Test-Path $ffi)) { throw "almatter_ffi.dll is missing from $publishDir" }

Write-Host '== Compiling the installer'
& $iscc "/DAppVersion=$version" (Join-Path $installerDir 'Almatter.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $installerDir "output\Almatter-Setup-$version.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "== Done: $setup ($sizeMb MB)"
