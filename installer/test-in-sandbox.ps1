# Tests the installer on a fresh Windows (no .NET 10, no Visual C++ runtime
# from other apps) using Windows Sandbox. Build the installer first with
# build-installer.ps1.
#
#   installer\test-in-sandbox.ps1          automatic: silent install + checks,
#                                          report in installer\sandbox-results
#   installer\test-in-sandbox.ps1 -Manual  opens the sandbox with the installer
#                                          on its desktop, to click through it
#   installer\test-in-sandbox.ps1 -UpgradeFrom <older Almatter-Setup-*.exe>
#                                          automatic, as an update: installs and
#                                          starts the older version first, then
#                                          runs the new installer over it and
#                                          lists what is left in the app folder
#
# For -UpgradeFrom, take the older setup from its GitHub release, e.g.
#   gh release download v0.15.0 --repo vincentnxi/almatter -p 'Almatter-Setup-*.exe' -D <folder>
# An older installer may install .NET first, which takes minutes in the
# Sandbox (each Microsoft package stalls on a signature check there).
#
# Windows Sandbox discards everything when closed, so each run starts fresh.
# Only one sandbox can be open at a time.

param(
    [switch]$Manual,
    [string]$UpgradeFrom
)

$ErrorActionPreference = 'Stop'

$installerDir = $PSScriptRoot
$outputDir = Join-Path $installerDir 'output'
$guestScripts = Join-Path $installerDir 'sandbox'
$resultsDir = Join-Path $installerDir 'sandbox-results'
$sandboxExe = "$env:windir\System32\WindowsSandbox.exe"

if (-not (Test-Path $sandboxExe)) { throw 'Windows Sandbox is not enabled (or the PC has not been restarted since).' }
if (-not (Get-ChildItem $outputDir -Filter 'Almatter-Setup-*.exe' -ErrorAction SilentlyContinue)) {
    throw 'No installer in installer\output: run build-installer.ps1 first.'
}

if ($UpgradeFrom) {
    if ($Manual) { throw '-UpgradeFrom runs automatically: leave out -Manual.' }
    if (-not (Test-Path $UpgradeFrom -PathType Leaf) -or (Split-Path $UpgradeFrom -Leaf) -notlike 'Almatter-Setup-*.exe') {
        throw "-UpgradeFrom must point to an Almatter-Setup-*.exe file: '$UpgradeFrom'"
    }
}

if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
New-Item -ItemType Directory $resultsDir | Out-Null

# The older installer gets its own read-only folder in the sandbox. It is
# copied under sandbox-results (rebuilt each run, ignored by git) so the
# sandbox never sees the rest of the folder it came from.
$previousDir = Join-Path $resultsDir 'previous'
New-Item -ItemType Directory $previousDir | Out-Null
if ($UpgradeFrom) {
    Copy-Item $UpgradeFrom $previousDir
}

if ($Manual) {
    $logon = '<LogonCommand><Command>explorer.exe C:\Almatter\setup</Command></LogonCommand>'
}
else {
    $logon = '<LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Almatter\test\run-in-sandbox.ps1</Command></LogonCommand>'
}

$config = @"
<Configuration>
  <MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$outputDir</HostFolder>
      <SandboxFolder>C:\Almatter\setup</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$guestScripts</HostFolder>
      <SandboxFolder>C:\Almatter\test</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$previousDir</HostFolder>
      <SandboxFolder>C:\Almatter\previous</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$resultsDir</HostFolder>
      <SandboxFolder>C:\Almatter\results</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  $logon
</Configuration>
"@

$wsb = Join-Path $resultsDir 'almatter-test.wsb'
Set-Content -Path $wsb -Value $config -Encoding UTF8
Start-Process $sandboxExe -ArgumentList "`"$wsb`""

if ($Manual) {
    Write-Host 'Sandbox opening: double-click the installer in the window that appears.'
}
else {
    if ($UpgradeFrom) { Write-Host "Testing an update from $(Split-Path $UpgradeFrom -Leaf)." }
    Write-Host "Sandbox opening; the report will be in $resultsDir\report.txt (ends with DONE)."
}
