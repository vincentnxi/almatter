# Runs INSIDE Windows Sandbox (a fresh Windows with no .NET 10), started by
# test-in-sandbox.ps1. Installs Almatter silently, then records what a
# first-time user's PC would go through into C:\Almatter\results\report.txt,
# which lands in installer\sandbox-results on the host.

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

$setup = Get-ChildItem 'C:\Almatter\setup' -Filter 'Almatter-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Report "Running $($setup.Name) /VERYSILENT"
$started = Get-Date
$process = Start-Process $setup.FullName -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$results\setup.log" -Wait -PassThru
Write-Report "Setup exit code: $($process.ExitCode) after $([int]((Get-Date) - $started).TotalSeconds) s"
Write-Report ".NET 10 after install: '$(Get-DotNet10)'"
Write-Report "Almatter.exe installed: $(Test-Path "$app\Almatter.exe")"

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
