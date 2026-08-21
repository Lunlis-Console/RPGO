param(
    [string]$Version,
    [string]$Config = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$clientProj = Join-Path $root "LostAndDivine.ClientMonoGame\LostAndDivine.ClientMonoGame.csproj"
$launcherProj = Join-Path $root "LostAndDivine.Launcher\LostAndDivine.Launcher.csproj"
$clientBuildDir = Join-Path $root "LostAndDivine.Server\client_build"
if (-not (Test-Path $clientBuildDir)) { New-Item -ItemType Directory -Path $clientBuildDir | Out-Null }
$manifestFile = Join-Path $clientBuildDir "manifest.json"
$filesDir = Join-Path $clientBuildDir "files"
$distDir = Join-Path $root "dist"
$publishDir = Join-Path $env:TEMP "lost-and-divine-client-publish"
$launcherPublishDir = Join-Path $env:TEMP "lost-and-divine-launcher-publish"
$combinedDir = Join-Path $env:TEMP "lost-and-divine-combined"

# --- version: param has priority, otherwise commit count (deterministic) ---
if (-not $Version) {
    try {
        $commitCount = (& git rev-list --count HEAD 2>$null | Out-String).Trim()
        if ($commitCount -match '^\d+$') {
            $Version = "0.1.$commitCount"
        } else {
            throw "git rev-list returned: '$commitCount'"
        }
    } catch {
        if (Test-Path (Join-Path $clientBuildDir "version.txt")) {
            $parts = ((Get-Content (Join-Path $clientBuildDir "version.txt")).Trim() -split '\.')
            if ($parts.Count -ge 3) { $parts[2] = [int]$parts[2] + 1; $Version = $parts -join '.' }
            else { $Version = "0.1.0" }
        } else { $Version = "0.1.0" }
    }
}
Write-Host "Client version: $Version"

# --- publish client (self-contained win-x64) ---
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Write-Host "Publishing client..."
dotnet publish $clientProj -c $Config -r win-x64 --self-contained true -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (client) failed ($LASTEXITCODE)" }

# --- publish launcher (self-contained win-x64) ---
if (Test-Path $launcherPublishDir) { Remove-Item -Recurse -Force $launcherPublishDir }
Write-Host "Publishing launcher..."
dotnet publish $launcherProj -c $Config -r win-x64 --self-contained true -o $launcherPublishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish (launcher) failed ($LASTEXITCODE)" }

# --- combined layout (client + launcher) ---
if (Test-Path $combinedDir) { Remove-Item -Recurse -Force $combinedDir }
New-Item -ItemType Directory -Path $combinedDir | Out-Null
# robocopy корректно сливает содержимое двух публикаций (Copy-Item -Recurse падает на совпадающих папках)
robocopy "$publishDir" "$combinedDir" /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
robocopy "$launcherPublishDir" "$combinedDir" /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null

# version.json (client state, NOT part of manifest; идёт только в дистрибутив/инсталлятор)
@{ version = $Version } | ConvertTo-Json | Set-Content (Join-Path $combinedDir "version.json") -Encoding UTF8

# --- manifest: sha256 of every file except version.json ---
$files = Get-ChildItem $combinedDir -Recurse -File | Where-Object { $_.Name -ne "version.json" }
$entries = foreach ($f in $files) {
    $rel = $f.FullName.Substring($combinedDir.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLowerInvariant()
    [pscustomobject]@{ Path = $rel; Size = $f.Length; Sha256 = $hash }
}
@{ Version = $Version; Files = @($entries) } | ConvertTo-Json -Depth 4 | Set-Content $manifestFile -Encoding UTF8
Write-Host "Manifest: $($entries.Count) files -> $manifestFile"

# --- sign manifest (private key, optional) ---
function Build-SignInput($Version, $Entries) {
    $sb = New-Object System.Text.StringBuilder
    $null = $sb.Append($Version)
    $null = $sb.Append([char]10)
    foreach ($e in ($Entries | Sort-Object { ($_.Path).ToLowerInvariant() })) {
        $null = $sb.Append($e.Path); $null = $sb.Append('|')
        $null = $sb.Append($e.Sha256); $null = $sb.Append('|')
        $null = $sb.Append([string]$e.Size); $null = $sb.Append([char]10)
    }
    [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
}
function Sign-Data($Bytes, $XmlKey) {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.FromXmlString($XmlKey)
    [Convert]::ToBase64String($rsa.SignData($Bytes, "SHA256"))
}
function Find-SigningKey {
    # 1. Явный путь через переменную окружения
    if ($env:LAD_SIGN_KEY_PATH -and (Test-Path $env:LAD_SIGN_KEY_PATH)) {
        return $env:LAD_SIGN_KEY_PATH
    }
    # 2. Локальный профиль (по умолчанию)
    $localXml = Join-Path $env:LOCALAPPDATA "LostAndDivine\sign_private.xml"
    if (Test-Path $localXml) { return $localXml }
    $localKey = Join-Path $env:LOCALAPPDATA "LostAndDivine\sign_private.key"
    if (Test-Path $localKey) { return $localKey }

    # 3. Любые файловые диски (флешки могут определяться и как Fixed
    #    после создания MBR-раздела): ключ рядом с маркером LAD_KEYDRIVE.txt
    $drives = Get-PSDrive -PSProvider FileSystem
    foreach ($d in $drives) {
        $root = $d.Root
        $marker = @(
            (Join-Path $root "LAD_KEYDRIVE.txt"),
            (Join-Path $root "LostAndDivine\LAD_KEYDRIVE.txt")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $marker) { continue }
        $markerDir = Split-Path $marker
        $found = @(
            (Join-Path $markerDir "sign_private.xml"),
            (Join-Path $markerDir "sign_private.key"),
            (Join-Path $root "LostAndDivine\sign_private.xml"),
            (Join-Path $root "LostAndDivine\sign_private.key")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($found) { return $found }
    }

    # 4. Запасной поиск: ключ прямо на диске без маркера
    foreach ($d in $drives) {
        $root = $d.Root
        $found = @(
            (Join-Path $root "LostAndDivine\sign_private.xml"),
            (Join-Path $root "LostAndDivine\sign_private.key"),
            (Join-Path $root "sign_private.xml"),
            (Join-Path $root "sign_private.key")
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($found) { return $found }
    }

    return $null
}

$keyPath = Find-SigningKey
if (Test-Path $keyPath) {
    $xml = Get-Content $keyPath -Raw
    $inputBytes = Build-SignInput -Version $Version -Entries $entries
    $sig = Sign-Data -Bytes $inputBytes -XmlKey $xml
    Set-Content (Join-Path $clientBuildDir "manifest.sig") $sig -Encoding ASCII
    Write-Host "Manifest SIGNED -> manifest.sig"
} else {
    Write-Warning "PRIVATE KEY NOT FOUND. Build is UNSIGNED; published clients will REJECT the update. Put the key at %LOCALAPPDATA%\LostAndDivine\sign_private.xml, on a USB drive (with LAD_KEYDRIVE.txt marker), or set LAD_SIGN_KEY_PATH. If the key is on a BitLocker drive, unlock it first."
}

# --- copy combined (без version.json) в client_build/files (сервер раздаёт) ---
if (Test-Path $filesDir) { Remove-Item -Recurse -Force $filesDir }
robocopy "$combinedDir" "$filesDir" /E /NFL /NDL /NJH /NJS /NC /NS /NP /XF version.json | Out-Null

# --- install_source: полный лейаут (включая version.json) для установщика ---
$installSource = Join-Path $clientBuildDir "install_source"
if (Test-Path $installSource) { Remove-Item -Recurse -Force $installSource }
robocopy "$combinedDir" "$installSource" /E /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null

# --- zip for a friend (включает version.json) ---
if (-not $NoZip) {
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zip = Join-Path $distDir "LostAndDivine-client-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Copy-Item (Join-Path $root "dist\README.txt") (Join-Path $combinedDir "README.txt") -ErrorAction SilentlyContinue
    Compress-Archive -Path $combinedDir -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
}

Write-Host "Done. Start the server so it serves client v$Version (client + launcher)."
