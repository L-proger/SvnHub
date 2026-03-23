Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-EnvValue {
    param(
        [string]$Path,
        [string]$Key
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith("#")) {
            continue
        }
        $parts = $trim.Split("=", 2)
        if ($parts.Length -eq 2 -and $parts[0].Trim() -eq $Key) {
            return $parts[1].Trim()
        }
    }
    return $null
}

function Read-HostWithDefault {
    param(
        [string]$Prompt,
        [string]$DefaultValue
    )
    $suffix = if ([string]::IsNullOrWhiteSpace($DefaultValue)) { "" } else { " [$DefaultValue]" }
    $value = Read-Host "$Prompt$suffix"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }
    return $value
}

function Format-EnvValue {
    param([string]$Value)
    if ($Value -match "\s|#") {
        $escaped = $Value.Replace('"', '\"')
        return '"' + $escaped + '"'
    }
    return $Value
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$envPath = Join-Path $scriptDir ".env"
$examplePath = Join-Path $scriptDir ".env.example"

$defaultData = Get-EnvValue -Path $envPath -Key "SVNHUB_DATA"
if (-not $defaultData) { $defaultData = Get-EnvValue -Path $examplePath -Key "SVNHUB_DATA" }

$defaultRepos = Get-EnvValue -Path $envPath -Key "SVNHUB_REPOS"
if (-not $defaultRepos) { $defaultRepos = Get-EnvValue -Path $examplePath -Key "SVNHUB_REPOS" }

$dataPath = Read-HostWithDefault -Prompt "SVNHUB_DATA (host path for SvnHub data)" -DefaultValue $defaultData
$reposPath = Read-HostWithDefault -Prompt "SVNHUB_REPOS (host path for SVN repositories)" -DefaultValue $defaultRepos

if ([string]::IsNullOrWhiteSpace($dataPath) -or [string]::IsNullOrWhiteSpace($reposPath)) {
    Write-Error "SVNHUB_DATA and SVNHUB_REPOS are required."
    exit 1
}

$skipChownInput = Read-Host "Skip chown on startup? (y/N)"
$skipChown = $skipChownInput -match "^(y|yes)$"

$lines = @(
    "# Host paths (bind mounts)",
    ("SVNHUB_DATA=" + (Format-EnvValue $dataPath)),
    ("SVNHUB_REPOS=" + (Format-EnvValue $reposPath)),
    "",
    "# Optional: avoid recursive chown on startup (recommended for large bind mounts after you fix perms once)"
)

if ($skipChown) {
    $lines += "SVNHUB_SKIP_CHOWN=1"
} else {
    $lines += "# SVNHUB_SKIP_CHOWN=1"
}

$lines | Set-Content -LiteralPath $envPath -Encoding UTF8

Write-Host "Starting SvnHub container..."

$composeArgs = @("compose", "--env-file", ".env", "up", "-d", "--build")
& docker @composeArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "docker compose failed, trying docker-compose..."
    & docker-compose --env-file .env up -d --build
    exit $LASTEXITCODE
}
exit 0
