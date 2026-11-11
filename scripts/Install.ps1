param([string]$PackageDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$PackageDirectory = (Resolve-Path $PackageDirectory).Path
$Files = 'Community.PowerToys.Run.Plugin.Walker.dll', 'Walker.Core.dll', 'FSharp.Core.dll', 'plugin.json'
foreach ($File in $Files) {
    if (-not (Test-Path (Join-Path $PackageDirectory $File))) {
        throw 'Extract the release ZIP and run Install.ps1 from the extracted powertoy-walker folder.'
    }
}
if (Get-Process -Name 'PowerToys.PowerLauncher' -ErrorAction SilentlyContinue) {
    throw 'Exit PowerToys from its system tray menu before installing.'
}
$Manifest = Get-Content (Join-Path $PackageDirectory 'plugin.json') -Raw | ConvertFrom-Json
if ($Manifest.ID -ne 'A8724BE7A2C2494FAD64D0B94CB3CF79') { throw 'Unexpected plugin package.' }
$PowerToysData = Join-Path $env:LOCALAPPDATA 'Microsoft/PowerToys'
$Target = Join-Path $PowerToysData 'PowerToys Run/Plugins/powertoy-walker'
if (Test-Path $Target) {
    $Backup = Join-Path $PowerToysData ('powertoy-walker-backups/' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
    New-Item -ItemType Directory -Force -Path (Split-Path $Backup -Parent) | Out-Null
    Move-Item -LiteralPath $Target -Destination $Backup
    Write-Host "Previous version backed up to $Backup"
}
New-Item -ItemType Directory -Force -Path "$Target/Images" | Out-Null
foreach ($File in $Files) {
    Copy-Item (Join-Path $PackageDirectory $File) $Target
}
Copy-Item (Join-Path $PackageDirectory 'Images/walker.png') "$Target/Images/"
Write-Host "Installed to $Target. Start PowerToys to load it."
