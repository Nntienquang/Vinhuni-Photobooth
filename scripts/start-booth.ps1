param([ValidateSet('mock','canon')][string]$Camera='mock',[ValidateSet('mock','windows')][string]$Printer='mock')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$env:CAMERA_PROVIDER=$Camera
$env:PRINT_PROVIDER=$Printer
if (-not $env:PHOTOBOOTH_DATA_DIR) { $env:PHOTOBOOTH_DATA_DIR=Join-Path $projectRoot 'data' }
Set-Location $projectRoot
dotnet run --project apps/bridge -c Release --no-build
