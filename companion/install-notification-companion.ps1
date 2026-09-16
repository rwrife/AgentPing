param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $env:LOCALAPPDATA 'AgentPing\NotificationCompanion'
if (Get-Process -Name AgentPing.Companion -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $output 'AgentPing.Companion.exe') }) {
    throw 'Exit AgentPing Companion from its tray menu before updating the installed app.'
}
# Build an unsigned Windows 11 demo package without adding signing certificates.
dotnet publish (Join-Path $PSScriptRoot 'AgentPing.Companion.Windows') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Companion build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'notification-package\AppxManifest.xml') -Destination $output
$manifestPath = Join-Path $output 'AppxManifest.xml'
[xml]$manifest = Get-Content -LiteralPath $manifestPath
$now = [DateTime]::UtcNow
$days = [int][Math]::Floor(($now - [DateTime]::new(2020,1,1)).TotalDays)
$minutes = [int][Math]::Floor($now.TimeOfDay.TotalMinutes)
$manifest.Package.Identity.Version = "1.0.$days.$minutes"
$manifest.Save($manifestPath)
$assets = Join-Path $output 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $repo 'assets\agentping-logo.png'))
try {
    foreach ($asset in @(@('Logo.png',150), @('SmallLogo.png',44), @('StoreLogo.png',50))) {
        $bitmap = [System.Drawing.Bitmap]::new([int]$asset[1], [int]$asset[1])
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.DrawImage($source, 0, 0, [int]$asset[1], [int]$asset[1])
            $bitmap.Save((Join-Path $assets $asset[0]), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$makeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (!$makeAppx) { throw 'Install the Windows 10/11 SDK (MakeAppx packaging tools) first.' }
$packagePath = Join-Path (Split-Path $output -Parent) 'NotificationCompanion.msix'
& $makeAppx.FullName pack /d $output /p $packagePath /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MSIX packaging failed.' }
Write-Host "Built: $packagePath"
if ($BuildOnly) { return }
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$isAdmin) {
    Write-Host 'Run this in an Administrator PowerShell window, using your normal Windows account:'
    Write-Host "Add-AppxPackage -Path '$packagePath' -AllowUnsigned"
    Write-Host 'Then launch AgentPing Companion from Start.'
    return
}
Add-AppxPackage -Path $packagePath -AllowUnsigned -ErrorAction Stop
$package = Get-AppxPackage -Name AgentPing.Companion
Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App" -WindowStyle Hidden
Write-Host 'Open Windows notifications, enable forwarding, allow Windows access, then select apps.'
