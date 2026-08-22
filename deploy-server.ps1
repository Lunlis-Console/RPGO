param(
    [Parameter(Mandatory)]
    [string]$ServerIp,
    [string]$Runtime = "linux-x64",
    [string]$User = "lostanddivine",
    [string]$RemoteDir = "/home/lostanddivine/lost-and-divine"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# --- P2-9: единый монотонный номер версии на весь деплой (клиент + установщик) ---
. "$root\version.ps1"
$deployVersion = Step-Version
Write-Host "=== Deploy version: $deployVersion ==="

Write-Host "=== 0. Building client ==="
# Запускаем дочерним процессом (powershell -File), иначе $LASTEXITCODE
# отражает код последней нативной команды внутри скрипта (например, robocopy
# возвращает 1 даже при успехе), а не код завершения самого скрипта.
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build-client-build.ps1" -RequireKey -Version $deployVersion
if ($LASTEXITCODE -ne 0) { throw "Client build failed (signing key on flash drive required)" }
Write-Host "  Client zip: dist\LostAndDivine-client-win-x64.zip (для раздачи друзьям)"

Write-Host "`n=== 0.1. Building installer (Setup.exe) ==="
# install_source уже собран выше (build-client-build.ps1) — не пересобираем клиент.
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build-installer.ps1" -SkipClientBuild -Version $deployVersion
if ($LASTEXITCODE -ne 0) {
    Write-Warning "Установщик не собран (нужен Inno Setup или ключ -SkipClientBuild). Сервер всё равно задеплоен."
}

Write-Host "`n=== 1. Building server for $Runtime ==="
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build-server.ps1" -Runtime $Runtime -NoZip
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$publishDir = Join-Path $env:TEMP "lost-and-divine-server-publish"

# Content (карты, секторы, тайлсеты) копируется целиком в build-server.ps1.
# Здесь только страховка на случай, если publish-папка без неё: копируем полную
# папку Content (с Sectors и Tilesets), а не только верхнеуровневые *.tmj.
$contentDir = Join-Path $publishDir "Content"
$contentSources = @(
    (Join-Path $root "LostAndDivine.Server\bin\Release\net8.0\$Runtime\Content"),
    (Join-Path $root "LostAndDivine.Server\Content"),
    (Join-Path $root "LostAndDivine.ClientMonoGame\Content")
)

$contentCopied = $false
foreach ($src in $contentSources) {
    if (Test-Path (Join-Path $src "Sectors")) {
        if (Test-Path $contentDir) { Remove-Item -Recurse -Force $contentDir }
        Copy-Item -Recurse $src $contentDir
        $sectorCount = @(Get-ChildItem (Join-Path $contentDir "Sectors") -Filter "*.tmj" -File).Count
        Write-Host "  Content/: maps, sectors ($sectorCount) and tilesets copied from $src"
        $contentCopied = $true
        break
    }
}
if (-not $contentCopied) {
    Write-Host "  WARNING: папка Content\Sectors не найдена — открытый мир будет без тайлов!"
}

# Copy client build for auto-updater
$clientBuildSrc = Join-Path $root "LostAndDivine.Server\client_build"
if (Test-Path $clientBuildSrc) {
    $clientBuildDest = Join-Path $publishDir "client_build"
    if (Test-Path $clientBuildDest) { Remove-Item -Recurse -Force $clientBuildDest }
    Copy-Item -Recurse $clientBuildSrc $clientBuildDest
    Write-Host "  Copied client_build/ for auto-updater"
}

# Create zip
$zipLocal = Join-Path $root "dist\LostAndDivine-server-linux-x64.zip"
if (Test-Path $zipLocal) { Remove-Item -Force $zipLocal }

# Verify Content exists before zipping
$checkContent = Join-Path $publishDir "Content"
if (-not (Test-Path $checkContent)) {
    Write-Host "  ERROR: Content folder missing! Creating..."
    New-Item -ItemType Directory -Path $checkContent | Out-Null
}
$contentFiles = Get-ChildItem $checkContent -File
if ($contentFiles.Count -eq 0) {
    Write-Host "  ERROR: No files in Content folder!"
} else {
    Write-Host "  Content folder: $($contentFiles.Count) files"
    foreach ($f in $contentFiles) { Write-Host "    $($f.Name)" }
}

Compress-Archive -Path "$publishDir\*" -DestinationPath $zipLocal -CompressionLevel Optimal
$sizeMB = [math]::Round((Get-Item $zipLocal).Length / 1MB, 1)
Write-Host "  Zip: $sizeMB MB"

Write-Host "`n=== 2. Uploading to $ServerIp ==="
scp $zipLocal ${User}@${ServerIp}:/home/${User}/
Write-Host "`n=== 3. Extracting and restarting on server ==="

$setupScript = Join-Path $env:TEMP "lost-and-divine-setup.sh"
@"
#!/bin/bash
set -e
BASE="/home/${User}/lost-and-divine"
cd /home/${User}

# Backup databases if they exist (P5: rollback .bak для content.db тоже)
if [ -d lost-and-divine ]; then
    cp lost-and-divine/game.db /home/${User}/game.db.bak 2>/dev/null || true
    cp lost-and-divine/content.db /home/${User}/content.db.bak 2>/dev/null || true
    rm -rf lost-and-divine
fi

mkdir -p lost-and-divine
python3 << 'PYEOF'
import zipfile, os
z = zipfile.ZipFile("/home/${User}/LostAndDivine-server-linux-x64.zip")
for f in z.namelist():
    target = os.path.join("/home/${User}/lost-and-divine", f.replace("\\", "/"))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if not f.endswith("/"):
        with open(target, "wb") as out:
            out.write(z.read(f))
z.close()
PYEOF

# Restore player accounts (game.db) from backup
if [ -f /home/${User}/game.db.bak ]; then
    cp /home/${User}/game.db.bak /home/${User}/lost-and-divine/game.db
    rm /home/${User}/game.db.bak
    echo "Restored game.db from backup"
fi
# content.db comes FRESH from the build (content is versioned in the repo).
# The old server content.db is kept as /home/${User}/content.db.bak for safety.
if [ -f /home/${User}/content.db.bak ]; then
    echo "Old content.db kept as /home/${User}/content.db.bak (NOT restored)"
fi

cd lost-and-divine
chmod +x LostAndDivine.Server start.sh
echo "Server files ready in /home/${User}/lost-and-divine/"
echo "Run: screen -S lost-and-divine ./start.sh"
'@ -replace "`r`n", "`n" | Set-Content -Path $setupScript -NoNewline

scp $setupScript ${User}@${ServerIp}:/home/${User}/lost-and-divine-setup.sh
ssh ${User}@${ServerIp} "sed -i 's/\r$//' /home/${User}/lost-and-divine-setup.sh; bash /home/${User}/lost-and-divine-setup.sh"

Write-Host "`n=== Done ==="
