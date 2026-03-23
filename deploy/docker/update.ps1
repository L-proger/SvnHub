Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$envPath = Join-Path $scriptDir ".env"
$useEnv = Test-Path -LiteralPath $envPath

if (-not $useEnv) {
    Write-Host "Warning: .env not found, using defaults from docker-compose.yml"
}

$composeArgs = @("compose")
if ($useEnv) { $composeArgs += @("--env-file", ".env") }
$composeArgs += @("up", "-d", "--build", "--remove-orphans")

Write-Host "Updating SvnHub container..."

& docker @composeArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed, trying docker-compose..."
    $fallbackArgs = @("up", "-d", "--build", "--remove-orphans")
    if ($useEnv) {
        & docker-compose --env-file .env @fallbackArgs
    } else {
        & docker-compose @fallbackArgs
    }
    exit $LASTEXITCODE
}
exit 0
