param(
    [string]$Version,
    [string]$Config = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$clientProj = Join-Path $root "LostAndDivine.ClientMonoGame\LostAndDivine.ClientMonoGame.csproj"
$clientBuildDir = Join-Path $root "LostAndDivine.Server\client_build"
if (-not (Test-Path $clientBuildDir)) { New-Item -ItemType Directory -Path $clientBuildDir | Out-Null }
$versionFile = Join-Path $clientBuildDir "version.txt"
$manifestFile = Join-Path $clientBuildDir "manifest.json"
$filesDir = Join-Path $clientBuildDir "files"
$distDir = Join-Path $root "dist"
$publishDir = Join-Path $env:TEMP "lost-and-divine-client-publish"

# --- version: param or auto-increment patch ---
if (-not $Version) {
    if (Test-Path $versionFile) {
        $parts = ((Get-Content $versionFile).Trim() -split '\.')
        if ($parts.Count -ge 3) {
            $parts[2] = [int]$parts[2] + 1
            $Version = $parts -join '.'
        } else {
            $Version = "0.1.0"
        }
    } else {
        $Version = "0.1.0"
    }
}
Write-Host "Client version: $Version"

# --- publish (self-contained win-x64) ---
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Write-Host "Publishing client..."
dotnet publish $clientProj -c $Config -r win-x64 --self-contained true -o $publishDir -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# --- version.json (client state, NOT part of manifest) ---
@{ version = $Version } | ConvertTo-Json | Set-Content (Join-Path $publishDir "version.json") -Encoding UTF8

# --- manifest: sha256 of every file except version.json ---
$files = Get-ChildItem $publishDir -Recurse -File | Where-Object { $_.Name -ne "version.json" }
$entries = foreach ($f in $files) {
    $rel = $f.FullName.Substring($publishDir.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -Algorithm SHA256 $f.FullName).Hash.ToLowerInvariant()
    [pscustomobject]@{ Path = $rel; Size = $f.Length; Sha256 = $hash }
}
@{ Version = $Version; Files = @($entries) } | ConvertTo-Json -Depth 4 | Set-Content $manifestFile -Encoding UTF8
Write-Host "Manifest: $($entries.Count) files -> $manifestFile"

# --- copy files into client_build (server serves them) ---
if (Test-Path $filesDir) { Remove-Item -Recurse -Force $filesDir }
Copy-Item -Recurse -Path $publishDir -Destination $filesDir
Remove-Item (Join-Path $filesDir "version.json") -ErrorAction SilentlyContinue

# --- version for next run ---
Set-Content $versionFile $Version

# --- zip for a friend ---
if (-not $NoZip) {
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $zip = Join-Path $distDir "LostAndDivine-client-win-x64.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Copy-Item (Join-Path $root "dist\README.txt") (Join-Path $publishDir "README.txt") -ErrorAction SilentlyContinue
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
}

Write-Host "Done. Start the server so it serves client v$Version."
