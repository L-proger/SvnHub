Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$envPath = Join-Path $scriptDir ".env"
$useEnv = Test-Path -LiteralPath $envPath

$removeVolumesInput = Read-Host "Remove volumes? This will delete container-managed volumes (y/N)"
$removeVolumes = $removeVolumesInput -match "^(y|yes)$"

$downArgs = @("compose")
if ($useEnv) { $downArgs += @("--env-file", ".env") }
$downArgs += "down"
if ($removeVolumes) { $downArgs += "-v" }

Write-Host "Stopping/removing SvnHub container..."
& docker @downArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed, trying docker-compose..."
    $fallbackArgs = @("down")
    if ($removeVolumes) { $fallbackArgs += "-v" }
    if ($useEnv) {
        & docker-compose --env-file .env @fallbackArgs
    } else {
        & docker-compose @fallbackArgs
    }
    exit $LASTEXITCODE
}
exit 0
