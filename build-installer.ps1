param(
    [string]$Version,
    [switch]$SkipClientBuild
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Version default = commit count (same as build-client-build.ps1) so the
# installer version matches the client version.
if (-not $Version) {
    try {
        $commitCount = (& git rev-list --count HEAD 2>$null | Out-String).Trim()
        if ($commitCount -match '^\d+$') { $Version = "0.1.$commitCount" }
        else { throw "git rev-list returned: '$commitCount'" }
    } catch { $Version = "0.1.0" }
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
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
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
& $iscc.FullName "/DMyAppVersion=$Version" "$iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Write-Host "Done: dist\LostAndDivine-Setup.exe"
