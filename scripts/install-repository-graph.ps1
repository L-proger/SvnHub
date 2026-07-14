param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$entryPoint = Join-Path $repoRoot "scripts\repository-graph-vendor-entry.js"
$destination = Join-Path $repoRoot "src\SvnHub.Web\wwwroot\lib\repository-graph"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("svnhub-repository-graph-" + [Guid]::NewGuid().ToString("N"))
$packages = @(
    @{ Name = "sigma"; Version = "3.0.3"; License = "LICENSE.txt" },
    @{ Name = "graphology"; Version = "0.26.0"; License = "LICENSE.txt" },
    @{ Name = "graphology-layout-forceatlas2"; Version = "0.10.1"; License = "LICENSE.txt" },
    @{ Name = "graphology-utils"; Version = "2.5.2"; License = "LICENSE.txt" },
    @{ Name = "events"; Version = "3.3.0"; License = "LICENSE" }
)

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $destination -Force | Out-Null

    npm install --prefix $tempRoot --no-save --ignore-scripts `
        sigma@3.0.3 `
        graphology@0.26.0 `
        graphology-layout-forceatlas2@0.10.1 `
        esbuild@0.28.1
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE."
    }

    $previousNodePath = $env:NODE_PATH
    $env:NODE_PATH = Join-Path $tempRoot "node_modules"
    try {
        $esbuild = Join-Path $tempRoot "node_modules\.bin\esbuild.cmd"
        & $esbuild $entryPoint `
            --bundle `
            --minify `
            --platform=browser `
            --target=es2020 `
            --format=iife `
            "--outfile=$(Join-Path $destination 'repository-graph-vendor.min.js')"
        if ($LASTEXITCODE -ne 0) {
            throw "esbuild failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        $env:NODE_PATH = $previousNodePath
    }

    $notices = foreach ($package in $packages) {
        "================================================================================"
        "$($package.Name) $($package.Version)"
        "================================================================================"
        Get-Content -LiteralPath (Join-Path $tempRoot "node_modules\$($package.Name)\$($package.License)") -Raw -Encoding utf8
        ""
    }
    Set-Content -LiteralPath (Join-Path $destination "THIRD-PARTY-NOTICES.txt") -Value $notices -Encoding utf8
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
