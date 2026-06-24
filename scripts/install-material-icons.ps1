Param(
    [string]$Version = "5.35.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot "src\SvnHub.Web\wwwroot"
$dest = Join-Path $webRoot "lib\material-icons"
$iconsDest = Join-Path $dest "icons"

Write-Host "Installing material-icon-theme $Version into $dest"

$tmp = Join-Path $env:TEMP ("material-icons-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    $tgzName = npm pack "material-icon-theme@$Version" --pack-destination $tmp
    $tgzPath = Join-Path $tmp $tgzName

    tar -xf $tgzPath -C $tmp
    $packageRoot = Join-Path $tmp "package"

    $jsonPath = Join-Path $packageRoot "dist\material-icons.json"
    $iconsPath = Join-Path $packageRoot "icons"
    if (-not (Test-Path $jsonPath)) {
        throw "Expected material-icons.json not found: $jsonPath"
    }
    if (-not (Test-Path $iconsPath)) {
        throw "Expected icons folder not found: $iconsPath"
    }

    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    if (Test-Path $iconsDest) {
        $destResolved = Resolve-Path -LiteralPath $dest
        $iconsResolved = Resolve-Path -LiteralPath $iconsDest
        if (-not ($iconsResolved.Path.StartsWith($destResolved.Path + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
            throw "Refusing to delete outside material icons folder: $($iconsResolved.Path)"
        }

        Remove-Item -Recurse -Force -LiteralPath $iconsResolved.Path
    }
    New-Item -ItemType Directory -Force -Path $iconsDest | Out-Null

    Copy-Item -Force $jsonPath (Join-Path $dest "material-icons.json")
    Copy-Item -Force (Join-Path $packageRoot "LICENSE") (Join-Path $dest "LICENSE")
    Copy-Item -Force (Join-Path $packageRoot "README.md") (Join-Path $dest "README.md")
    Copy-Item -Force (Join-Path $iconsPath "*.svg") $iconsDest

    Write-Host "Done."
} finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
