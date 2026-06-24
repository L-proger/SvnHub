Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$envPath = Join-Path $scriptDir ".env"

if (-not (Test-Path -LiteralPath $envPath)) {
    Write-Host "Warning: .env not found, using defaults from docker-compose.yml"
}

$composeArgs = @("compose", "up", "-d", "--build", "--remove-orphans")

Write-Host "Updating SvnHub container..."

& docker @composeArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed, trying docker-compose..."
    & docker-compose up -d --build --remove-orphans
    exit $LASTEXITCODE
}
exit 0
