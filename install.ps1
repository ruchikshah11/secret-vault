<#
.SYNOPSIS
    Builds and installs SecretVault for the current Windows user (no admin rights needed).

.DESCRIPTION
    Publishes a self-contained, single-file SecretVault.exe (runs without a separate .NET install),
    copies it to %LOCALAPPDATA%\Programs\SecretVault, creates Start Menu and Desktop shortcuts, and
    registers the app under Settings > Apps so it can be uninstalled like any other program.

    Your vault is NOT part of the installation. It lives in %LOCALAPPDATA%\SecretVault and is never
    modified or removed by this script — not even by -Uninstall.

.EXAMPLE
    .\install.ps1                  # build + install + shortcuts
.EXAMPLE
    .\install.ps1 -Launch          # ...and start SecretVault afterwards
.EXAMPLE
    .\install.ps1 -NoDesktopShortcut
.EXAMPLE
    .\install.ps1 -Uninstall       # remove the app, shortcuts and Apps entry (vault is kept)
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Launch,
    [switch]$NoDesktopShortcut,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\SecretVault')
)

$ErrorActionPreference = 'Stop'
$AppName       = 'SecretVault'
$Version       = '1.0.0'
$UninstallKey  = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
$StartMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
$DesktopLink   = Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"
$VaultDir      = Join-Path $env:LOCALAPPDATA $AppName

function Write-Step([string]$Text) { Write-Host "  > $Text" -ForegroundColor Cyan }

function Stop-RunningApp {
    $running = Get-Process -Name $AppName -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) {
        Write-Step "Closing the installed $AppName that is running"
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }
}

function New-Shortcut([string]$Path, [string]$Target) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.WorkingDirectory = Split-Path $Target
    $link.IconLocation = "$Target,0"
    $link.Description = $AppName
    $link.Save()
}

# ------------------------------------------------------------------ uninstall
if ($Uninstall) {
    Write-Host "`nUninstalling $AppName" -ForegroundColor White
    Stop-RunningApp
    foreach ($link in $StartMenuLink, $DesktopLink) {
        if (Test-Path $link) { Remove-Item $link -Force; Write-Step "Removed shortcut $link" }
    }
    if (Test-Path $UninstallKey) { Remove-Item $UninstallKey -Recurse -Force; Write-Step 'Removed Settings > Apps entry' }
    if (Test-Path $InstallDir) {
        # The uninstaller itself may be running from $InstallDir; delete everything we can now.
        Get-ChildItem $InstallDir -Force | Where-Object { $_.FullName -ne $PSCommandPath } | Remove-Item -Recurse -Force
        Write-Step "Removed $InstallDir"
        if ($PSCommandPath -and $PSCommandPath.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase)) {
            Start-Process -WindowStyle Hidden cmd.exe "/c timeout /t 2 >nul & rmdir /s /q `"$InstallDir`""
        } else {
            Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Host "`n$AppName was uninstalled." -ForegroundColor Green
    Write-Host "Your vault was kept: $VaultDir" -ForegroundColor Yellow
    Write-Host "(Delete that folder yourself only if you no longer need your passwords.)"
    return
}

# ------------------------------------------------------------------ install
Write-Host "`nInstalling $AppName $Version" -ForegroundColor White

$repo = $PSScriptRoot
$project = Join-Path $repo 'SecretVault\SecretVault.csproj'
if (-not (Test-Path $project)) {
    throw "Can't find $project. Run install.ps1 from the repository root (next to SecretVault.slnx)."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is required to build SecretVault. Install it from https://dotnet.microsoft.com/download'
}

$publishDir = Join-Path $repo 'dist'   # ready-to-share build: dist\SecretVault.exe
Write-Step 'Building a self-contained release (this can take a minute)'
# Replace only the desktop build; dist\ also holds the Android app (SecretVault.apk).
Remove-Item (Join-Path $publishDir "$AppName.exe") -Force -ErrorAction SilentlyContinue
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o $publishDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed (dotnet publish exit code $LASTEXITCODE)." }

$exeSource = Join-Path $publishDir "$AppName.exe"
if (-not (Test-Path $exeSource)) { throw "Build produced no $AppName.exe in $publishDir." }

Stop-RunningApp
Write-Step "Copying to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $publishDir '*') $InstallDir -Recurse -Force
Copy-Item $PSCommandPath (Join-Path $InstallDir 'install.ps1') -Force   # used by the uninstaller
$exe = Join-Path $InstallDir "$AppName.exe"

Write-Step 'Creating Start Menu shortcut'
New-Shortcut $StartMenuLink $exe
if (-not $NoDesktopShortcut) {
    Write-Step 'Creating Desktop shortcut'
    New-Shortcut $DesktopLink $exe
}

Write-Step 'Registering in Settings > Apps'
New-Item -Path $UninstallKey -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\install.ps1`" -Uninstall"
$values = @{
    DisplayName     = $AppName
    DisplayVersion  = $Version
    Publisher       = 'SecretVault'
    DisplayIcon     = "$exe,0"
    InstallLocation = $InstallDir
    UninstallString = $uninstallCmd
    NoModify        = 1
    NoRepair        = 1
}
foreach ($name in $values.Keys) {
    $type = if ($values[$name] -is [int]) { 'DWord' } else { 'String' }
    New-ItemProperty -Path $UninstallKey -Name $name -Value $values[$name] -PropertyType $type -Force | Out-Null
}
$sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
New-ItemProperty -Path $UninstallKey -Name EstimatedSize -Value $sizeKb -PropertyType DWord -Force | Out-Null

Write-Host "`n$AppName $Version installed." -ForegroundColor Green
Write-Host "  App:   $exe"
Write-Host "  Vault: $VaultDir  (unchanged)"
Write-Host "  Start it from the Start Menu$(if (-not $NoDesktopShortcut) { ' or the Desktop shortcut' })."
Write-Host "  Uninstall: Settings > Apps > $AppName, or .\install.ps1 -Uninstall"

if ($Launch) { Start-Process $exe }
