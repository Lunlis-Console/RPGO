param(
    [string]$Version,
    [switch]$SkipClientBuild
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Version: if provided, use as-is. Otherwise bump the monotonic build number
# (P2-9) so each published installer/client gets a strictly greater version.
. "$PSScriptRoot\version.ps1"
if (-not $Version) {
    $Version = Step-Version
}
Write-Host "Installer version: $Version"

if (-not $SkipClientBuild) {
    # Build client + launcher first (into client_build/install_source)
    Write-Host "== Building client and launcher =="
    & "$root\build-client-build.ps1" -Version $Version
    if ($LASTEXITCODE -ne 0) { throw "build-client-build.ps1 failed" }
} else {
    Write-Host "== Skipping client build (install_source already present) =="
}

# Find ISCC (Inno Setup Compiler)
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $pf86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $candidates = @(
        (Join-Path $pf86 "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $iscc = $c; break } }
}
if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found. Download free: https://jrsoftware.org/isdl.php"
    Write-Warning "Then run: iscc.exe '$root\installer.iss'"
    exit 1
}

Write-Host "== Compiling installer =="
$iss = Join-Path $root "installer.iss"
$isccPath = if ($iscc -is [System.Management.Automation.CommandInfo]) { $iscc.Source } else { $iscc }
& $isccPath "/DMyAppVersion=$Version" "$iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Write-Host "Done: dist\LostAndDivine-Setup.exe"
