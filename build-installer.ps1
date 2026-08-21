param(
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Сначала собираем клиент + лаунчер (в client_build/install_source)
Write-Host "== Сборка клиента и лаунчера =="
& "$root\build-client-build.ps1" -Version $Version
if ($LASTEXITCODE -ne 0) { throw "build-client-build.ps1 failed" }

# Ищем ISCC (Inno Setup Compiler)
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { $iscc = $c; break } }
}
if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) не найден. Скачайте бесплатно: https://jrsoftware.org/isdl.php"
    Write-Warning "Затем запустите: iscc.exe '$root\installer.iss'"
    exit 1
}

Write-Host "== Компиляция установщика =="
$iss = Join-Path $root "installer.iss"
& $iscc.FullName "/DMyAppVersion=$Version" "$iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Write-Host "Готово: dist\LostAndDivine-Setup.exe"
