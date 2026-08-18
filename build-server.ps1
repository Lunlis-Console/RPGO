param(
    [string]$Runtime = "win-x64",
    [string]$Config = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$serverProj = Join-Path $root "LostAndDivine.Server\LostAndDivine.Server.csproj"
$distDir = Join-Path $root "dist"
$publishDir = Join-Path $env:TEMP "lost-and-divine-server-publish"

$runtimeLabel = if ($Runtime -eq "linux-x64") { "linux" } else { "win" }

Write-Host "Building server for $Runtime..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish $serverProj -c $Config -r $Runtime --self-contained true -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# Copy runtime data
$serverBin = Join-Path $root "LostAndDivine.Server\bin\$Config\net8.0\$Runtime"
$gameDbSrc = $null
$contentDbSrc = $null

if (Test-Path (Join-Path $serverBin "game.db")) {
    $gameDbSrc = Join-Path $serverBin "game.db"
} elseif (Test-Path (Join-Path $root "LostAndDivine.Server\game.db")) {
    $gameDbSrc = Join-Path $root "LostAndDivine.Server\game.db"
}

if (Test-Path (Join-Path $serverBin "content.db")) {
    $contentDbSrc = Join-Path $serverBin "content.db"
} elseif (Test-Path (Join-Path $root "LostAndDivine.Server\content.db")) {
    $contentDbSrc = Join-Path $root "LostAndDivine.Server\content.db"
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

# dotnet publish не включает папку Content (карты, секторы, тайлсеты) в вывод,
# поэтому копируем её целиком из результата сборки. Источник в bin (bin\...\Content)
# создаётся таргетом CopyContent в LostAndDivine.Server.csproj: туда попадают только
# нужные серверу файлы — *.tmj верхнего уровня, Sectors\*.tmj и Tilesets\*.png.
$contentSrc = $null
if (Test-Path (Join-Path $serverBin "Content")) {
    $contentSrc = Join-Path $serverBin "Content"
} elseif (Test-Path (Join-Path $root "LostAndDivine.Server\Content")) {
    $contentSrc = Join-Path $root "LostAndDivine.Server\Content"
} elseif (Test-Path (Join-Path $root "LostAndDivine.ClientMonoGame\Content")) {
    $contentSrc = Join-Path $root "LostAndDivine.ClientMonoGame\Content"
}

if ($contentSrc) {
    $contentDest = Join-Path $publishDir "Content"
    if (Test-Path $contentDest) { Remove-Item -Recurse -Force $contentDest }
    Copy-Item -Recurse $contentSrc $contentDest
    $tmjCount = @(Get-ChildItem -Path $contentDest -Filter "*.tmj" -File -Recurse).Count
    $sectorCount = @(Get-ChildItem -Path (Join-Path $contentDest "Sectors") -Filter "*.tmj" -File -ErrorAction SilentlyContinue).Count
    Write-Host "  Copied Content/ to publish: $tmjCount .tmj files (sectors: $sectorCount)"
} else {
    Write-Host "  WARNING: Content source not found!"
}

# Start script
if ($Runtime -eq "linux-x64") {
    $startContent = @'
#!/bin/bash
cd "$(dirname "$0")"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
./LostAndDivine.Server
'@
    Set-Content -Path (Join-Path $publishDir "start.sh") -Value ($startContent -replace "`r`n", "`n") -NoNewline
    Write-Host "  Created start.sh"
} else {
    $startContent = @'
@echo off
cd /d "%~dp0"
LostAndDivine.Server.exe
pause
'@
    Set-Content -Path (Join-Path $publishDir "start.bat") -Value $startContent -NoNewline
    Write-Host "  Created start.bat"
}

# Zip
if (-not $NoZip) {
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zipName = "LostAndDivine-server-$runtimeLabel-x64.zip"
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
    Write-Host "     On Linux first: chmod +x start.sh && chmod +x LostAndDivine.Server"
}
Write-Host "  3. Ensure port 7777 is open"
