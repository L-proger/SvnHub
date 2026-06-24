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

$defaultUid = Get-EnvValue -Path $envPath -Key "SVNHUB_UID"
if (-not $defaultUid) { $defaultUid = Get-EnvValue -Path $examplePath -Key "SVNHUB_UID" }
if (-not $defaultUid) { $defaultUid = "10001" }

$defaultGid = Get-EnvValue -Path $envPath -Key "SVNHUB_GID"
if (-not $defaultGid) { $defaultGid = Get-EnvValue -Path $examplePath -Key "SVNHUB_GID" }
if (-not $defaultGid) { $defaultGid = "10001" }

$dataPath = Read-HostWithDefault -Prompt "SVNHUB_DATA (host path for SvnHub data)" -DefaultValue $defaultData
$reposPath = Read-HostWithDefault -Prompt "SVNHUB_REPOS (host path for SVN repositories)" -DefaultValue $defaultRepos
$serviceUid = Read-HostWithDefault -Prompt "SVNHUB_UID (container service user UID)" -DefaultValue $defaultUid
$serviceGid = Read-HostWithDefault -Prompt "SVNHUB_GID (container service group GID)" -DefaultValue $defaultGid

if ([string]::IsNullOrWhiteSpace($dataPath) -or [string]::IsNullOrWhiteSpace($reposPath) -or
    [string]::IsNullOrWhiteSpace($serviceUid) -or [string]::IsNullOrWhiteSpace($serviceGid)) {
    Write-Error "SVNHUB_DATA, SVNHUB_REPOS, SVNHUB_UID, and SVNHUB_GID are required."
    exit 1
}

if ($serviceUid -notmatch "^\d+$" -or $serviceGid -notmatch "^\d+$") {
    Write-Error "SVNHUB_UID and SVNHUB_GID must be numeric."
    exit 1
}

$fixOwnershipInput = Read-Host "Recursively chown mounted directories on startup? (y/N)"
$fixOwnership = $fixOwnershipInput -match "^(y|yes)$"

$lines = @(
    "# Host paths (bind mounts)",
    ("SVNHUB_DATA=" + (Format-EnvValue $dataPath)),
    ("SVNHUB_REPOS=" + (Format-EnvValue $reposPath)),
    "",
    "# Service identity inside the container. Linux bind mounts use these numeric IDs.",
    ("SVNHUB_UID=" + $serviceUid),
    ("SVNHUB_GID=" + $serviceGid),
    "",
    "# Optional: recursively chown both mounted directories at startup.",
    "# Leave disabled for existing production repositories unless you intentionally want this."
)

if ($fixOwnership) {
    $lines += "SVNHUB_FIX_OWNERSHIP=1"
} else {
    $lines += "SVNHUB_FIX_OWNERSHIP=0"
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
