# version.ps1 — единый источник монотонной версии сборки (P2-9).
# Версия = "MAJOR.MINOR.BUILDNUMBER"; BUILDNUMBER только растёт и не зависит
# от git-истории, поэтому порядок обновлений остаётся корректным при rebase/сжатии.

$VersionFile = Join-Path $PSScriptRoot "version.txt"

function Get-Version {
    if (Test-Path $VersionFile) {
        $raw = (Get-Content $VersionFile -Raw).Trim()
        if ($raw -match '^\d+\.\d+\.\d+$') { return $raw }
    }
    return "0.1.0"
}

function Step-Version {
    $cur = Get-Version
    $parts = $cur -split '\.'
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $build = [int]$parts[2]
    $build++
    $next = "$major.$minor.$build"
    Set-Content -Path $VersionFile -Value $next -Encoding UTF8 -NoNewline
    return $next
}
