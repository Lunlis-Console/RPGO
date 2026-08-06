param(
    [string]$Runtime = "win-x64",
    [string]$Config = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$serverProj = Join-Path $root "RPGO.Server\RPGO.Server.csproj"
$distDir = Join-Path $root "dist"
$publishDir = Join-Path $env:TEMP "rpgo-server-publish"

$runtimeLabel = if ($Runtime -eq "linux-x64") { "linux" } else { "win" }

Write-Host "Building server for $Runtime..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish $serverProj -c $Config -r $Runtime --self-contained true -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Copy runtime data
$serverBin = Join-Path $root "RPGO.Server\bin\$Config\net8.0\$Runtime"
$gameDbSrc = $null
$contentDbSrc = $null

if (Test-Path (Join-Path $serverBin "game.db")) {
    $gameDbSrc = Join-Path $serverBin "game.db"
} elseif (Test-Path (Join-Path $root "RPGO.Server\game.db")) {
    $gameDbSrc = Join-Path $root "RPGO.Server\game.db"
}

if (Test-Path (Join-Path $serverBin "content.db")) {
    $contentDbSrc = Join-Path $serverBin "content.db"
} elseif (Test-Path (Join-Path $root "RPGO.Server\content.db")) {
    $contentDbSrc = Join-Path $root "RPGO.Server\content.db"
}

if ($gameDbSrc) {
    Copy-Item $gameDbSrc $publishDir -ErrorAction SilentlyContinue
    Write-Host "  Copied game.db"
} else {
    Write-Host "  WARNING: game.db not found - will be created on first run"
}

if ($contentDbSrc) {
    Copy-Item $contentDbSrc $publishDir -ErrorAction SilentlyContinue
    Write-Host "  Copied content.db"
}

$contentSrc = $null
if (Test-Path (Join-Path $serverBin "Content")) {
    $contentSrc = Join-Path $serverBin "Content"
} elseif (Test-Path (Join-Path $root "RPGO.Server\Content")) {
    $contentSrc = Join-Path $root "RPGO.Server\Content"
} elseif (Test-Path (Join-Path $root "RPGO.ClientMonoGame\Content")) {
    $contentSrc = Join-Path $root "RPGO.ClientMonoGame\Content"
}

if ($contentSrc) {
    $contentDest = Join-Path $publishDir "Content"
    if (-not (Test-Path $contentDest)) { New-Item -ItemType Directory -Path $contentDest | Out-Null }
    $tmjFiles = Get-ChildItem -Path $contentSrc -Filter "*.tmj" -File
    foreach ($f in $tmjFiles) {
        Copy-Item $f.FullName $contentDest
    }
    Write-Host "  Copied $($tmjFiles.Count) .tmj files to Content/"
}

# Start script
if ($Runtime -eq "linux-x64") {
    $startContent = @'
#!/bin/bash
cd "$(dirname "$0")"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
./RPGO.Server
'@
    Set-Content -Path (Join-Path $publishDir "start.sh") -Value $startContent -NoNewline
    Write-Host "  Created start.sh"
} else {
    $startContent = @'
@echo off
cd /d "%~dp0"
RPGO.Server.exe
pause
'@
    Set-Content -Path (Join-Path $publishDir "start.bat") -Value $startContent -NoNewline
    Write-Host "  Created start.bat"
}

# Zip
if (-not $NoZip) {
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zipName = "RPGO-server-$runtimeLabel-x64.zip"
    $zip = Join-Path $distDir $zipName
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zip -CompressionLevel Optimal
    $sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Zip: $zipName ($sizeMB MB)"
}

Write-Host ""
Write-Host "Done. Server for $Runtime is ready."
Write-Host "  1. Extract zip on target machine"
Write-Host "  2. Run start.sh (Linux) or start.bat (Windows)"
if ($Runtime -eq "linux-x64") {
    Write-Host "     On Linux first: chmod +x start.sh && chmod +x RPGO.Server"
}
Write-Host "  3. Ensure port 7777 is open"
