# Build the Vue frontend + C# backend into .\output as a single runnable app.
#
#   .\build.ps1                framework-dependent (needs .NET 8 runtime installed)
#   .\build.ps1 win-x64        self-contained single .exe (no runtime needed)
param([string]$Rid = "")
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Out  = Join-Path $Root "output"

Write-Host "==> Cleaning $Out"
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }

Write-Host "==> Building frontend (Vite)"
Push-Location (Join-Path $Root "frontend")
npm install
npm run build
Pop-Location

Write-Host "==> Publishing backend (.NET)"
Push-Location (Join-Path $Root "backend")
if ($Rid -ne "") {
  Write-Host "    self-contained single file for $Rid"
  dotnet publish -c Release -r $Rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Out
} else {
  Write-Host "    framework-dependent (requires the .NET 8 runtime)"
  dotnet publish -c Release -o $Out
}
Pop-Location

Write-Host "==> Copying frontend build into wwwroot"
$www = Join-Path $Out "wwwroot"
if (Test-Path $www) { Remove-Item -Recurse -Force $www }
New-Item -ItemType Directory -Force -Path $www | Out-Null
Copy-Item -Recurse -Force (Join-Path $Root "frontend\dist\*") $www

Write-Host "==> Writing launcher"
$bat = @"
@echo off
cd /d "%~dp0"
echo Starting Branch Merger... a browser tab will open shortly.
echo Keep this window open. Close it to stop the app.
BranchMerger.Api.exe
"@
Set-Content -Path (Join-Path $Out "Start.bat") -Value $bat -Encoding ASCII

Write-Host ""
Write-Host "Done. Everything is in: $Out"
Write-Host "Run it: double-click output\Start.bat"
Write-Host "Then browse to http://localhost:5080 (it also opens automatically)."
