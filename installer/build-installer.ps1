# Builds installer\output\Almatter-Setup-<version>.exe from scratch:
#   1. the Rust core in release mode (almatter_ffi.dll),
#   2. the app published framework-dependent for win-x64 into installer\publish,
#   3. the Inno Setup script, with the version taken from Almatter.App.csproj
#      and the newest .NET 10 runtime looked up and verified (see below).
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

# The .NET runtime Setup installs when it is missing: always the newest 10.0.x
# at build time, so every installer hands out the latest security fixes
# without a manual bump. Setup itself still checks the download against a
# SHA-256 baked in here, so nothing unverified ever runs on a user's PC.
# What earns that SHA-256 our trust: the file matches the SHA-512 Microsoft
# publishes in its release list, and carries a valid Microsoft signature.
Write-Host '== Latest .NET 10 runtime'
$ProgressPreference = 'SilentlyContinue'   # Windows PowerShell downloads crawl with the progress bar on
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$dotnetVersion = $metadata.'latest-runtime'
$release = $metadata.releases | Where-Object { $_.runtime.version -eq $dotnetVersion } | Select-Object -First 1
$file = $release.runtime.files | Where-Object { $_.name -eq 'dotnet-runtime-win-x64.exe' } | Select-Object -First 1
# These values end up inside the Inno Setup script, so anything off the
# expected shape stops the build rather than being pasted in.
if ($dotnetVersion -notmatch '^10\.0\.\d+$') { throw "Unexpected .NET runtime version '$dotnetVersion'" }
$dotnetUrl = "https://builds.dotnet.microsoft.com/dotnet/Runtime/$dotnetVersion/dotnet-runtime-$dotnetVersion-win-x64.exe"
if (-not $file -or $file.url -ne $dotnetUrl) { throw "Unexpected .NET runtime download '$($file.url)', expected $dotnetUrl" }
if ($file.hash -notmatch '^[0-9a-fA-F]{128}$') { throw "Unexpected SHA-512 '$($file.hash)' for $dotnetUrl" }

$dotnetFile = Join-Path ([IO.Path]::GetTempPath()) "almatter-dotnet-runtime-$dotnetVersion-win-x64.exe"
try {
    Invoke-WebRequest $dotnetUrl -OutFile $dotnetFile -UseBasicParsing
    $sha512 = (Get-FileHash $dotnetFile -Algorithm SHA512).Hash
    if ($sha512 -ne $file.hash) { throw "$dotnetUrl has SHA-512 $sha512, Microsoft lists $($file.hash)" }
    $signature = Get-AuthenticodeSignature $dotnetFile
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') {
        throw "$dotnetUrl is not validly signed by Microsoft ($($signature.Status), $($signature.SignerCertificate.Subject))"
    }
    $dotnetSha256 = (Get-FileHash $dotnetFile -Algorithm SHA256).Hash.ToLowerInvariant()
}
finally {
    Remove-Item $dotnetFile -ErrorAction SilentlyContinue
}
Write-Host "   $dotnetVersion, SHA-256 $dotnetSha256"

Write-Host '== Compiling the installer'
& $iscc "/DAppVersion=$version" "/DDotNetVersion=$dotnetVersion" "/DDotNetUrl=$dotnetUrl" "/DDotNetSha256=$dotnetSha256" (Join-Path $installerDir 'Almatter.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

$setup = Join-Path $installerDir "output\Almatter-Setup-$version.exe"
$sizeMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "== Done: $setup ($sizeMb MB)"
