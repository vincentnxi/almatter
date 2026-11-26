<#
.SYNOPSIS
    Signs Almatter installers for the app's automatic updates, with a key
    kept on this PC only.

.DESCRIPTION
    The app downloads a new version by itself and runs it only if the
    installer carries a signature from this key (see almatter-core's
    updates.rs). The key's secret half must never reach GitHub: that is the
    whole point, since the signature is there to protect users even if the
    GitHub account were taken over.

    The secret is stored protected by Windows (DPAPI, this user account on
    this PC) under %LOCALAPPDATA%\Almatter-release — not under
    %LOCALAPPDATA%\Almatter, which uninstalling the app may delete. It is
    handed to the signing program on standard input, never on a command
    line, and never printed.

      sign-update.ps1 -NewKey             create the key (once), print its public half
      sign-update.ps1 -ExportBackup -BackupTo E:\
                                          write a backup copy, in the clear, to a USB stick
      sign-update.ps1 -Restore E:\almatter-update-key-BACKUP.txt
                                          put a backup back (new PC, new Windows account)
      sign-update.ps1 -PublicKey          print the public half
      sign-update.ps1 -Installer path\Almatter-Setup-0.18.0.exe -Version 0.18.0
                                          write Almatter-Setup-0.18.0.exe.sig next to it

    Losing the key (no backup) does not break anything already installed,
    but the versions out there will refuse updates signed with a new key:
    everyone would have to install one version by hand again.
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(ParameterSetName = 'New', Mandatory)] [switch] $NewKey,
    [Parameter(ParameterSetName = 'Export', Mandatory)] [switch] $ExportBackup,
    [Parameter(ParameterSetName = 'Export', Mandatory)] [string] $BackupTo,
    [Parameter(ParameterSetName = 'Restore', Mandatory)] [string] $Restore,
    [Parameter(ParameterSetName = 'Public', Mandatory)] [switch] $PublicKey,
    [Parameter(ParameterSetName = 'Sign', Mandatory)] [string] $Installer,
    [Parameter(ParameterSetName = 'Sign', Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$keyDir = Join-Path $env:LOCALAPPDATA 'Almatter-release'
$keyFile = Join-Path $keyDir 'update-signing-key.dpapi'
$coreDir = Join-Path $PSScriptRoot '..\core'
# Ties the protected file to this use, on top of the Windows account.
$entropy = [Text.Encoding]::UTF8.GetBytes('Almatter update signing key v1')
$backupName = 'almatter-update-key-BACKUP.txt'

function Save-SecretKey([byte[]] $seed) {
    if ($seed.Length -ne 32) { throw 'A key is 32 bytes.' }
    New-Item -ItemType Directory -Force $keyDir | Out-Null
    $protected = [Security.Cryptography.ProtectedData]::Protect($seed, $entropy, 'CurrentUser')
    [IO.File]::WriteAllBytes($keyFile, $protected)
}

function Read-SecretKeyHex {
    if (-not (Test-Path $keyFile)) { throw "No signing key on this PC ($keyFile). Create one with -NewKey, or -Restore a backup." }
    $seed = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyFile), $entropy, 'CurrentUser')
    try { return -join ($seed | ForEach-Object { $_.ToString('x2') }) }
    finally { [Array]::Clear($seed, 0, $seed.Length) }
}

function ConvertFrom-KeyHex([string] $hex) {
    $hex = $hex.Trim()
    if ($hex -notmatch '^[0-9a-fA-F]{64}$') { throw 'Not a key (64 hex digits expected).' }
    [byte[]] $bytes = for ($i = 0; $i -lt 64; $i += 2) { [Convert]::ToByte($hex.Substring($i, 2), 16) }
    return , $bytes
}

# Runs the signing program with the secret on standard input.
function Invoke-Signer([string[]] $arguments) {
    Push-Location $coreDir
    try {
        # Built from the source in this checkout, then run directly: the
        # secret goes to the program itself, not through cargo.
        & cargo build --quiet -p almatter-core --example sign_update
        if ($LASTEXITCODE -ne 0) { throw "Building sign_update failed ($LASTEXITCODE)" }
        $exe = Join-Path $coreDir 'target\debug\examples\sign_update.exe'
        $output = Read-SecretKeyHex | & $exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "sign_update failed ($LASTEXITCODE)" }
        return $output
    }
    finally { Pop-Location }
}

switch ($PSCmdlet.ParameterSetName) {
    'New' {
        if (Test-Path $keyFile) { throw "A signing key already exists ($keyFile). Refusing to replace it." }
        $seed = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($seed) } finally { $rng.Dispose() }
        Save-SecretKey $seed
        [Array]::Clear($seed, 0, 32)
        Write-Host 'Key created. Public key (goes into updates.rs, UPDATE_PUBLIC_KEYS):'
        Invoke-Signer @('public-key')
        Write-Host "Now make a backup: sign-update.ps1 -ExportBackup -BackupTo <USB stick>"
    }
    'Export' {
        if (-not (Test-Path $BackupTo -PathType Container)) { throw "No such folder: $BackupTo" }
        $target = Join-Path $BackupTo $backupName
        if (Test-Path $target) { throw "$target already exists." }
        $lines = @(
            'Almatter - cle secrete de signature des mises a jour',
            'A garder hors ligne (cle USB rangee, gestionnaire de mots de passe). Ne jamais la mettre sur GitHub, OneDrive ou dans un e-mail.',
            'Pour la remettre sur un PC : tools\sign-update.ps1 -Restore <ce fichier>',
            '',
            (Read-SecretKeyHex)
        )
        [IO.File]::WriteAllLines($target, $lines)
        Write-Host "Backup written to $target"
    }
    'Restore' {
        if (Test-Path $keyFile) { throw "A signing key already exists ($keyFile). Refusing to replace it." }
        $hex = (Get-Content -LiteralPath $Restore | Where-Object { $_ -match '^\s*[0-9a-fA-F]{64}\s*$' } | Select-Object -First 1)
        if (-not $hex) { throw "No key found in $Restore" }
        $seed = ConvertFrom-KeyHex $hex
        Save-SecretKey $seed
        [Array]::Clear($seed, 0, 32)
        Write-Host 'Key restored. Public key:'
        Invoke-Signer @('public-key')
    }
    'Public' {
        Invoke-Signer @('public-key')
    }
    'Sign' {
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Not a version number: $Version" }
        $path = (Resolve-Path -LiteralPath $Installer).Path
        Invoke-Signer @('sign', $path, $Version)
    }
}
