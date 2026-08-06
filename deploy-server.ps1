param(
    [Parameter(Mandatory)]
    [string]$ServerIp,
    [string]$Runtime = "linux-x64",
    [string]$User = "root",
    [string]$RemoteDir = "/root/rpgo-server"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "=== 1. Building server for $Runtime ==="
& "$root\build-server.ps1" -Runtime $Runtime -NoZip
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$publishDir = Join-Path $env:TEMP "rpgo-server-publish"

# Ensure Content folder exists with maps
$contentDir = Join-Path $publishDir "Content"
$mapSources = @(
    (Join-Path $root "RPGO.Server\Content"),
    (Join-Path $root "RPGO.Server\bin\Release\net8.0\$Runtime\Content"),
    (Join-Path $root "RPGO.ClientMonoGame\Content")
)

$tmjCopied = $false
foreach ($src in $mapSources) {
    if (Test-Path $src) {
        $files = Get-ChildItem -Path $src -Filter "*.tmj" -File
        if ($files.Count -gt 0) {
            if (-not (Test-Path $contentDir)) { New-Item -ItemType Directory -Path $contentDir | Out-Null }
            foreach ($f in $files) {
                Copy-Item $f.FullName $contentDir -Force
            }
            Write-Host "  Maps: $($files.Count) .tmj files from $src"
            $tmjCopied = $true
            break
        }
    }
}
if (-not $tmjCopied) {
    Write-Host "  WARNING: No .tmj map files found!"
}

# Create zip
$zipLocal = Join-Path $root "dist\RPGO-server-linux-x64.zip"
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

$setupScript = Join-Path $env:TEMP "rpgo-setup.sh"
@'
#!/bin/bash
cd /root

# Backup databases if they exist
if [ -d rpgo-server ]; then
    cp rpgo-server/game.db /root/game.db.bak 2>/dev/null
    cp rpgo-server/content.db /root/content.db.bak 2>/dev/null
    rm -rf rpgo-server
fi

mkdir -p rpgo-server
python3 << 'PYEOF'
import zipfile, os
z = zipfile.ZipFile("/root/RPGO-server-linux-x64.zip")
for f in z.namelist():
    target = os.path.join("/root/rpgo-server", f.replace("\\", "/"))
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if not f.endswith("/"):
        with open(target, "wb") as out:
            out.write(z.read(f))
z.close()
PYEOF

# Restore databases from backup
if [ -f /root/game.db.bak ]; then
    cp /root/game.db.bak /root/rpgo-server/game.db
    rm /root/game.db.bak
    echo "Restored game.db from backup"
fi
if [ -f /root/content.db.bak ]; then
    cp /root/content.db.bak /root/rpgo-server/content.db
    rm /root/content.db.bak
    echo "Restored content.db from backup"
fi

cd rpgo-server
chmod +x RPGO.Server start.sh
echo "Server files ready in /root/rpgo-server/"
echo "Run: screen -S rpgo ./start.sh"
'@ | Set-Content -Path $setupScript -NoNewline

scp $setupScript root@${ServerIp}:/root/rpgo-setup.sh
ssh root@${ServerIp} "bash /root/rpgo-setup.sh"

Write-Host "`n=== Done ==="
