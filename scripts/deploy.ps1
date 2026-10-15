<#
.SYNOPSIS
  Deploys the current version to the production server.
.DESCRIPTION
  Default: builds the api and web images locally and streams them to the server (docker save | ssh docker load).
  -BuildOnServer: sends the committed source (git archive HEAD) to the server and builds the images there
                  (slower on a 1-CPU server, but needs no disk space on this machine).
  In both cases docker-compose files and deploy/ (Caddyfile) are updated in /opt/apollodata and the stack is
  restarted; database migrations run automatically. The server's own .env is never overwritten.
.EXAMPLE
  powershell -File scripts/deploy.ps1
  powershell -File scripts/deploy.ps1 -BuildOnServer
  powershell -File scripts/deploy.ps1 -Server 147.228.121.223 -User root -SkipBuild
#>
param(
    [string]$Server = '147.228.121.223',
    [string]$User = 'root',
    [string]$RemoteDir = '/opt/apollodata',
    [switch]$BuildOnServer,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$target = "$User@$Server"

if ($BuildOnServer) {
    if (git status --porcelain) { Write-Host 'Note: uncommitted changes are NOT deployed (git archive HEAD).' -ForegroundColor Yellow }
    Write-Host "==> Sending source ($(git rev-parse --short HEAD)) to $target" -ForegroundColor Cyan
    # cmd.exe pipes binary data unchanged (Windows PowerShell 5.1 would re-encode it).
    cmd /c "git archive --format=tar HEAD | ssh -C $target tar -x -C $RemoteDir"
    if ($LASTEXITCODE -ne 0) { throw 'Copying the source failed.' }
    Write-Host '==> Building and restarting on the server (this takes a few minutes)' -ForegroundColor Cyan
    ssh $target "cd $RemoteDir && docker compose build && docker compose up -d && docker image prune -f >/dev/null && docker builder prune -f >/dev/null && docker compose ps --format '{{.Service}}: {{.Status}}'"
    if ($LASTEXITCODE -ne 0) { throw "Deployment failed - see: ssh $target 'cd $RemoteDir && docker compose logs api'" }
    Write-Host "==> Done ($Server)" -ForegroundColor Green
    exit 0
}

docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker is not running - start Docker Desktop and try again (or use -BuildOnServer).' }

$freeGb = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if (-not $SkipBuild -and $freeGb -lt 5) {
    throw "Only $freeGb GB free on C:. Free some space, run 'docker builder prune -f', or deploy with -BuildOnServer."
}

if (-not $SkipBuild) {
    Write-Host '==> Building images' -ForegroundColor Cyan
    docker compose build api web
    if ($LASTEXITCODE -ne 0) { throw 'docker compose build failed.' }
}

Write-Host "==> Copying images to $target" -ForegroundColor Cyan
cmd /c "docker save apollodata-api:latest apollodata-web:latest | ssh -C $target docker load"
if ($LASTEXITCODE -ne 0) { throw 'Copying images failed.' }

Write-Host '==> Restarting the stack' -ForegroundColor Cyan
cmd /c "git archive --format=tar HEAD docker-compose.yml docker-compose.https.yml deploy | ssh $target tar -x -C $RemoteDir"
ssh $target "cd $RemoteDir && docker compose up -d --no-build && docker image prune -f >/dev/null && docker compose ps --format '{{.Service}}: {{.Status}}'"
if ($LASTEXITCODE -ne 0) { throw "Restart failed - see: ssh $target 'cd $RemoteDir && docker compose logs api'" }

if (-not $SkipBuild) { docker builder prune -f | Out-Null }
Write-Host "==> Done ($Server)" -ForegroundColor Green
