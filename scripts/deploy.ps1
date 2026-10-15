<#
.SYNOPSIS
  Deploys the current version to the production server (Docker images are built locally and copied over SSH).
.DESCRIPTION
  1. Builds the api and web images locally (unless -SkipBuild).
  2. Streams them to the server with `docker save | ssh docker load` (the server never needs the source code).
  3. Copies docker-compose.yml to /opt/apollodata and restarts the stack; database migrations run automatically.
  The server keeps its own /opt/apollodata/.env (it is never overwritten by this script).
.EXAMPLE
  powershell -File scripts/deploy.ps1
  powershell -File scripts/deploy.ps1 -Server 147.228.121.223 -User root -SkipBuild
#>
param(
    [string]$Server = '147.228.121.223',
    [string]$User = 'root',
    [string]$RemoteDir = '/opt/apollodata',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$target = "$User@$Server"

docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker is not running - start Docker Desktop and try again.' }

$freeGb = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if (-not $SkipBuild -and $freeGb -lt 5) {
    throw "Only $freeGb GB free on C:. Free some space (or run: docker builder prune -f) before building images."
}

if (-not $SkipBuild) {
    Write-Host '==> Building images' -ForegroundColor Cyan
    docker compose build api web
    if ($LASTEXITCODE -ne 0) { throw 'docker compose build failed.' }
}

Write-Host "==> Copying images to $target" -ForegroundColor Cyan
# cmd.exe pipes binary data unchanged (Windows PowerShell 5.1 would re-encode it).
cmd /c "docker save apollodata-api:latest apollodata-web:latest | ssh -C $target docker load"
if ($LASTEXITCODE -ne 0) { throw 'Copying images failed.' }

Write-Host '==> Restarting the stack' -ForegroundColor Cyan
scp -q docker-compose.yml "${target}:$RemoteDir/docker-compose.yml"
ssh $target "cd $RemoteDir && docker compose up -d --no-build && docker image prune -f >/dev/null && docker compose ps --format '{{.Service}}: {{.Status}}'"
if ($LASTEXITCODE -ne 0) { throw 'Restart failed - see: ssh ' + $target + " 'cd $RemoteDir && docker compose logs api'" }

if (-not $SkipBuild) { docker builder prune -f | Out-Null }
Write-Host "==> Done: http://$Server" -ForegroundColor Green
