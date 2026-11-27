# Runs INSIDE Windows Sandbox (a fresh Windows with no .NET 10), started by
# test-in-sandbox.ps1. Installs Almatter silently, then records what a
# first-time user's PC would go through into C:\Almatter\results\report.txt,
# which lands in installer\sandbox-results on the host.
#
# With an older setup in C:\Almatter\previous (test-in-sandbox.ps1
# -UpgradeFrom), that version is installed and left running first, so the
# report shows an update as a user would get it: whether Setup closed the
# running app, and which files are left in the app folder afterwards.

$results = 'C:\Almatter\results'
$report = Join-Path $results 'report.txt'
$app = "$env:LOCALAPPDATA\Programs\Almatter"
$netCoreDir = "$env:ProgramFiles\dotnet\shared\Microsoft.NETCore.App"

function Write-Report([string]$line) {
    $stamped = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $line
    Add-Content -Path $report -Value $stamped -Encoding UTF8
}

function Get-DotNet10 {
    if (Test-Path $netCoreDir) {
        (Get-ChildItem $netCoreDir -Directory -Filter '10.*' | Select-Object -ExpandProperty Name) -join ', '
    }
}

Write-Report "Windows: $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).BuildNumber)"
Write-Report "vcruntime140.dll in System32 before install: $(Test-Path "$env:windir\System32\vcruntime140.dll")"
Write-Report ".NET 10 before install: '$(Get-DotNet10)'"

function Write-AppFolder([string]$label) {
    $files = Get-ChildItem $app -File -ErrorAction SilentlyContinue
    $byType = ($files | Group-Object Extension | Sort-Object Name | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
    Write-Report "$label`: $($files.Count) files in the app folder ($byType)"
}

$previous = Get-ChildItem 'C:\Almatter\previous' -Filter 'Almatter-Setup-*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
$oldAlmatter = $null
if ($previous) {
    Write-Report "UPGRADE TEST: first installing the older $($previous.Name) /VERYSILENT"
    $started = Get-Date
    $process = Start-Process $previous.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$results\setup-previous.log" -Wait -PassThru
    Write-Report "Older setup exit code: $($process.ExitCode) after $([int]((Get-Date) - $started).TotalSeconds) s"
    Write-Report ".NET 10 after the older install: '$(Get-DotNet10)'"
    Write-AppFolder 'Before the update'
    $oldVersion = (Get-Item "$app\Almatter.exe" -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
    Write-Report "Older Almatter.exe version: '$oldVersion'"

    # Left running, as on a user's PC: Setup has to close it to replace
    # Almatter.exe (CloseApplications=yes in Almatter.iss).
    if (Test-Path "$app\Almatter.exe") {
        $oldAlmatter = Start-Process "$app\Almatter.exe" -PassThru
        Start-Sleep -Seconds 15
        $oldAlmatter.Refresh()
        Write-Report "Older Almatter running before the update: $(-not $oldAlmatter.HasExited)"
    }
}

$setup =Get-ChildItem 'C:\Almatter\setup' -Filter 'Almatter-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
# test-in-sandbox.ps1 -AsAutoUpdate: the arguments the app's "Restart to
# update" passes (MainViewModel.Updates.cs), after which Almatter must be
# started again by Setup itself.
$asAutoUpdate = Test-Path 'C:\Almatter\previous\as-auto-update'
$setupArguments = if ($asAutoUpdate) { '/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/AUTOUPDATE=1' } else { '/VERYSILENT', '/SUPPRESSMSGBOXES' }
Write-Report "Running $($setup.Name) $($setupArguments -join ' ')"
$started = Get-Date
# WaitForExit rather than -Wait: -Wait also waits for whatever Setup starts,
# and with /AUTOUPDATE=1 that is Almatter itself, which keeps running.
$process = Start-Process $setup.FullName -ArgumentList ($setupArguments + "/LOG=$results\setup.log") -PassThru
$process.WaitForExit()
Write-Report "Setup exit code: $($process.ExitCode) after $([int]((Get-Date) - $started).TotalSeconds) s"
Write-Report ".NET 10 after install: '$(Get-DotNet10)'"
Write-Report "Almatter.exe installed: $(Test-Path "$app\Almatter.exe")"
if ($asAutoUpdate) {
    Start-Sleep -Seconds 10
    $restarted = Get-Process Almatter -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq "$app\Almatter.exe" -and (-not $oldAlmatter -or $_.Id -ne $oldAlmatter.Id) }
    Write-Report "AUTO-UPDATE: Almatter started again by Setup: $([bool]$restarted)"
    # Out of the way of the start-up check further down, which starts its own.
    $restarted | Stop-Process -Force -ErrorAction SilentlyContinue
}

if ($previous) {
    if ($oldAlmatter) {
        $oldAlmatter.Refresh()
        Write-Report "Older Almatter closed by Setup: $($oldAlmatter.HasExited)"
    }
    Write-Report "Almatter.exe version after the update: '$((Get-Item "$app\Almatter.exe" -ErrorAction SilentlyContinue).VersionInfo.ProductVersion)'"
    Write-AppFolder 'After the update'
    Get-ChildItem $app -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object {
        Write-Report ("  {0} ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB))
    }
    # What a Native AOT install should hold: the exe, the four native
    # libraries and the licence. Anything else came from the older version.
    $expected = 'Almatter.exe', 'almatter-icon.ico', 'almatter_ffi.dll','av_libglesv2.dll', 'libHarfBuzzSharp.dll', 'libSkiaSharp.dll', 'LICENSE.txt', 'unins000.dat', 'unins000.exe'
    $leftovers = Get-ChildItem $app -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin $expected }
    if ($leftovers) {
        Write-Report "LEFTOVERS from the older version: $(($leftovers | ForEach-Object Name) -join ', ')"
    }
    else {
        Write-Report 'No leftovers from the older version.'
    }
}

# Shortcuts show almatter-icon.ico, which the app rewrites in the tint picked
# in its settings; a later install must leave that file alone.
$iconFile = "$app\almatter-icon.ico"
$startShortcut = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Almatter.lnk"
Write-Report "almatter-icon.ico installed: $(Test-Path $iconFile)"
if (Test-Path $startShortcut) {
    Write-Report "Start menu shortcut icon: '$((New-Object -ComObject WScript.Shell).CreateShortcut($startShortcut).IconLocation)'"
}
else {
    Write-Report 'Start menu shortcut: MISSING'
}
if (Test-Path $iconFile) {
    # Stands in for the app having written another tint.
    [IO.File]::AppendAllText($iconFile, 'x')
    $before = (Get-FileHash $iconFile).Hash
    $process = Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$results\setup-again.log" -PassThru
    $process.WaitForExit()
    Write-Report "Second install exit code: $($process.ExitCode); chosen icon kept: $((Get-FileHash $iconFile).Hash -eq $before)"
    Get-Process Almatter -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

# The app only loads the Rust core after sign-in, so a missing native
# dependency would not show at startup: load the DLL directly instead.
Add-Type -Namespace SandboxTest -Name Kernel32 -MemberDefinition @'
[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr LoadLibraryW(string path);
'@
$handle = [SandboxTest.Kernel32]::LoadLibraryW("$app\almatter_ffi.dll")
$lastError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
if ($handle -ne [IntPtr]::Zero) {
    Write-Report 'almatter_ffi.dll loads: True'
}
else {
    Write-Report "almatter_ffi.dll loads: False (Win32 error $lastError; 126 = a DLL it depends on is missing)"
}

if (Test-Path "$app\Almatter.exe") {
    $almatter = Start-Process "$app\Almatter.exe" -PassThru
    Start-Sleep -Seconds 20
    $almatter.Refresh()
    if ($almatter.HasExited) {
        Write-Report "Almatter exited within 20 s, exit code $($almatter.ExitCode)"
    }
    else {
        Write-Report "Almatter running after 20 s, window '$($almatter.MainWindowTitle)'"
    }
    $crashLog = "$env:LOCALAPPDATA\Almatter\crash.log"
    if (Test-Path $crashLog) { Copy-Item $crashLog $results }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1026; StartTime = $started } -ErrorAction SilentlyContinue |
        ForEach-Object { Write-Report "Event $($_.Id): $($_.Message)" }
}

Write-Report 'DONE'
