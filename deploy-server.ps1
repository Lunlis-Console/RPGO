param(
    [Parameter(Mandatory)]
    [string]$ServerIp,
    [string]$Runtime = "linux-x64",
    [string]$User = "root",
    [string]$RemoteDir = "/root/lost-and-divine"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== 0. Building client ==="
# Запускаем дочерним процессом (powershell -File), иначе $LASTEXITCODE
# отражает код последней нативной команды внутри скрипта (например, robocopy
# возвращает 1 даже при успехе), а не код завершения самого скрипта.
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build-client-build.ps1" -RequireKey
if ($LASTEXITCODE -ne 0) { throw "Client build failed (signing key on flash drive required)" }
Write-Host "  Client zip: dist\LostAndDivine-client-win-x64.zip (для раздачи друзьям)"

Write-Host "`n=== 0.1. Building installer (Setup.exe) ==="
# install_source уже собран выше (build-client-build.ps1) — не пересобираем клиент.
powershell -NoProfile -ExecutionPolicy Bypass -File "$root\build-installer.ps1" -SkipClientBuild
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
scp $zipLocal ${User}@${ServerIp}:/root/
Write-Host "`n=== 3. Extracting and restarting on server ==="

$setupScript = Join-Path $env:TEMP "lost-and-divine-setup.sh"
@'
#!/bin/bash
cd /root

# Backup databases if they exist
if [ -d lost-and-divine ]; then
    cp lost-and-divine/game.db /root/game.db.bak 2>/dev/null
    cp lost-and-divine/content.db /root/content.db.bak 2>/dev/null
    rm -rf lost-and-divine
fi

mkdir -p lost-and-divine
python3 << 'PYEOF'
import zipfile, os
z = zipfile.ZipFile("/root/LostAndDivine-server-linux-x64.zip")
for f in z.namelist():
    target = os.path.join("/root/lost-and-divine", f.replace("\\", "/"))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if not f.endswith("/"):
        with open(target, "wb") as out:
            out.write(z.read(f))
z.close()
PYEOF

# Restore player accounts (game.db) from backup
if [ -f /root/game.db.bak ]; then
    cp /root/game.db.bak /root/lost-and-divine/game.db
    rm /root/game.db.bak
    echo "Restored game.db from backup"
fi
# content.db comes FRESH from the build (content is versioned in the repo).
# The old server content.db is kept as /root/content.db.bak for safety.
if [ -f /root/content.db.bak ]; then
    echo "Old content.db kept as /root/content.db.bak (NOT restored)"
fi

cd lost-and-divine
chmod +x LostAndDivine.Server start.sh
echo "Server files ready in /root/lost-and-divine/"
echo "Run: screen -S lost-and-divine ./start.sh"
'@ -replace "`r`n", "`n" | Set-Content -Path $setupScript -NoNewline

scp $setupScript root@${ServerIp}:/root/lost-and-divine-setup.sh
ssh root@${ServerIp} "sed -i 's/\r$//' /root/lost-and-divine-setup.sh; bash /root/lost-and-divine-setup.sh"

Write-Host "`n=== Done ==="
