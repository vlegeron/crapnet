<#
.SYNOPSIS
    Downloads WinDivert and places its runtime files next to the Crapnet executable.

.DESCRIPTION
    WinDivert is the packet capture driver Crapnet builds on, the same one clumsy uses. It ships
    as a signed kernel driver under LGPLv3, so it is fetched at setup time rather than vendored
    into this repository.

.PARAMETER Destination
    Where to place WinDivert.dll and WinDivert64.sys. Defaults to the debug build output.

.PARAMETER Version
    WinDivert release to fetch.

.EXAMPLE
    ./scripts/fetch-windivert.ps1
    ./scripts/fetch-windivert.ps1 -Destination ./publish
#>
[CmdletBinding()]
param(
    [string]$Destination = "src/Crapnet.App/bin/Debug/net8.0-windows10.0.19041.0",
    [string]$Version = "2.2.2"
)

$ErrorActionPreference = 'Stop'

$archiveName = "WinDivert-$Version-A"
$url = "https://github.com/basil00/WinDivert/releases/download/v$Version/$archiveName.zip"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "crapnet-windivert-$Version"

Write-Host "Fetching WinDivert $Version..." -ForegroundColor Cyan

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null

$zipPath = Join-Path $staging "windivert.zip"
Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
Expand-Archive -Path $zipPath -DestinationPath $staging -Force

# The 64-bit payload is the only one we want: Crapnet targets x64 and the driver must match.
$source = Join-Path $staging "$archiveName/x64"
if (-not (Test-Path $source)) {
    throw "The archive did not contain an x64 folder. Check whether the WinDivert layout changed for $Version."
}

if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

foreach ($file in @('WinDivert.dll', 'WinDivert64.sys')) {
    $from = Join-Path $source $file
    if (-not (Test-Path $from)) { throw "$file is missing from the WinDivert archive." }
    Copy-Item $from -Destination $Destination -Force
    Write-Host "  -> $(Join-Path $Destination $file)"
}

Remove-Item $staging -Recurse -Force

Write-Host "Done. WinDivert is in place." -ForegroundColor Green
Write-Host "Crapnet must run elevated: the driver is loaded on first use." -ForegroundColor Yellow
