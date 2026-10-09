$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
node scripts/pnpm-run.mjs --filter '@booth/web' build
if ($LASTEXITCODE -ne 0) { throw 'Web build failed' }
node scripts/copy-web.mjs
dotnet publish apps/bridge -c Release -r win-x64 --self-contained true -o artifacts/bridge
if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed' }
Write-Output 'Set PHOTOBOOTH_DATA_DIR to an absolute writable data directory before launching artifacts/bridge/Booth.Bridge.exe.'
