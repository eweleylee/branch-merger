# Build an installer + auto-update release for Branch Merger using Velopack.
#
#   .\pack.ps1                        # build + pack locally into .\releases
#   .\pack.ps1 -Upload -Token <PAT>   # ...then create/upload the GitHub release
#
# Prerequisites (one time):
#   dotnet tool install -g vpk
#
# The version comes from <Version> in backend\BranchMerger.Api.csproj -- that is the
# single source of truth. Bump it there before packing a new release.
#
# User data (settings.json / schedules.json / notifications.json) lives in
# %APPDATA%\BranchMerger and is NEVER touched by install or update.
param(
  [string]$Rid   = "win-x64",
  [switch]$Upload,
  [string]$Token = "",
  [string]$Repo  = "eweleylee/branch-merger"
)
$ErrorActionPreference = "Stop"

$Root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$Backend   = Join-Path $Root "backend"
$Frontend  = Join-Path $Root "frontend"
$Publish   = Join-Path $Root "publish"
$Releases  = Join-Path $Root "releases"
$PackId    = "BranchMerger"
$MainExe   = "BranchMerger.Api.exe"
$Csproj    = Join-Path $Backend "BranchMerger.Api.csproj"

# --- Read the version from the csproj -----------------------------------------
$verMatch = Select-String -Path $Csproj -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1
if (-not $verMatch) { throw "Could not find <Version> in $Csproj" }
$Version = $verMatch.Matches[0].Groups[1].Value.Trim()
Write-Host "==> Packing Branch Merger v$Version ($Rid)" -ForegroundColor Cyan

# --- Ensure vpk is available --------------------------------------------------
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
  throw "The 'vpk' tool is not installed. Run:  dotnet tool install -g vpk"
}

# --- Frontend build -----------------------------------------------------------
Write-Host "==> Building frontend (Vite)"
Push-Location $Frontend
npm install
npm run build
Pop-Location

# --- Backend publish (self-contained, NOT single-file -- Velopack repackages) -
Write-Host "==> Publishing backend ($Rid, self-contained)"
if (Test-Path $Publish) { Remove-Item -Recurse -Force $Publish }
Push-Location $Backend
dotnet publish -c Release -r $Rid --self-contained true -p:PublishSingleFile=false -o $Publish
Pop-Location

# --- Copy the built UI into wwwroot -------------------------------------------
Write-Host "==> Copying frontend build into wwwroot"
$www = Join-Path $Publish "wwwroot"
if (Test-Path $www) { Remove-Item -Recurse -Force $www }
New-Item -ItemType Directory -Force -Path $www | Out-Null
Copy-Item -Recurse -Force (Join-Path $Frontend "dist\*") $www

# --- Pull prior releases so Velopack can build a delta (best-effort) ----------
Write-Host "==> vpk download (prior releases, for delta - ok if none yet)"
$dl = @("download", "github", "--repoUrl", "https://github.com/$Repo", "--outputDir", $Releases)
if ($Token) { $dl += @("--token", $Token) }
try { vpk @dl } catch { }   # first-ever release has nothing to download; ignore

# --- Velopack pack: produces Setup.exe + *-full.nupkg + releases.win.json ------
Write-Host "==> vpk pack"
vpk pack `
  --packId      $PackId `
  --packVersion $Version `
  --packDir     $Publish `
  --mainExe     $MainExe `
  --packTitle   "Branch Merger" `
  --outputDir   $Releases

Write-Host ""
Write-Host "Release assets are in: $Releases" -ForegroundColor Green

# --- Optional: create + upload the GitHub release -----------------------------
if ($Upload) {
  if (-not $Token) { throw "Upload requires -Token (a GitHub PAT with 'repo' scope)" }
  Write-Host "==> Uploading to GitHub release v$Version"
  vpk upload github `
    --repoUrl     "https://github.com/$Repo" `
    --publish `
    --releaseName "v$Version" `
    --tag         "v$Version" `
    --token       $Token
  Write-Host "Done. Release v$Version published." -ForegroundColor Green
} else {
  Write-Host ""
  Write-Host "Not uploaded. To publish this release to GitHub, either:" -ForegroundColor Yellow
  Write-Host "  .\pack.ps1 -Upload -Token <PAT>"
  Write-Host "  or create a release for tag v$Version in the GitHub UI and upload every file from the releases folder."
}
