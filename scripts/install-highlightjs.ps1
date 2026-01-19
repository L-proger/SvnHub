Param(
    [string]$Version = "11.9.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot "src\SvnHub.Web\wwwroot"
$dest = Join-Path $webRoot "lib\highlightjs"

Write-Host "Installing highlight.js $Version into $dest"

$tmp = Join-Path $env:TEMP ("highlightjs-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    $zipPath = Join-Path $tmp "cdn-release.zip"
    $url = "https://github.com/highlightjs/cdn-release/archive/refs/tags/$Version.zip"
    Invoke-WebRequest -Uri $url -OutFile $zipPath

    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force

    $extracted = Join-Path $tmp ("cdn-release-" + $Version + "\build")
    if (-not (Test-Path $extracted)) {
        throw "Expected build folder not found: $extracted"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $dest "languages") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $dest "styles") | Out-Null

    Copy-Item -Force (Join-Path $extracted "highlight.min.js") (Join-Path $dest "highlight.min.js")
    Copy-Item -Force (Join-Path $extracted "styles\github-dark.min.css") (Join-Path $dest "styles\github-dark.min.css")

    Copy-Item -Force (Join-Path $extracted "languages\*.min.js") (Join-Path $dest "languages")

    Write-Host "Done."
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
