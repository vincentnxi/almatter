# Tests the installer on a fresh Windows (no .NET 10, no Visual C++ runtime
# from other apps) using Windows Sandbox. Build the installer first with
# build-installer.ps1.
#
#   installer\test-in-sandbox.ps1          automatic: silent install + checks,
#                                          report in installer\sandbox-results
#   installer\test-in-sandbox.ps1 -Manual  opens the sandbox with the installer
#                                          on its desktop, to click through it
#
# Windows Sandbox discards everything when closed, so each run starts fresh.
# Only one sandbox can be open at a time.

param([switch]$Manual)

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

if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
New-Item -ItemType Directory $resultsDir | Out-Null

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
    Write-Host "Sandbox opening; the report will be in $resultsDir\report.txt (ends with DONE)."
}
