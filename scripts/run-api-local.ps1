<#
.SYNOPSIS
  Runs the API with `dotnet run` against the PostgreSQL container from docker-compose.dev.yml.
.DESCRIPTION
  1. docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d db
  2. scripts/run-api-local.ps1          (API on http://localhost:5080, Swagger on /swagger)
  3. cd frontend; npm run dev           (SPA on http://localhost:5173, proxies /api and /hubs)
  Values are read from .env; the database host is mapped to localhost. Email uses the SMTP settings from .env.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) { throw ".env not found - run scripts/init-env.ps1 first." }

$values = @{}
foreach ($line in [IO.File]::ReadAllLines($envFile, [Text.Encoding]::UTF8)) {
    if ($line -match '^\s*([A-Z_][A-Z0-9_]*)=(.*)$') { $values[$Matches[1]] = $Matches[2] }
}
foreach ($key in $values.Keys) { [Environment]::SetEnvironmentVariable($key, $values[$key], 'Process') }

$db = if ($values.POSTGRES_DB) { $values.POSTGRES_DB } else { 'apollodata' }
$user = if ($values.POSTGRES_USER) { $values.POSTGRES_USER } else { 'apollo' }
$port = if ($values.POSTGRES_PORT) { $values.POSTGRES_PORT } else { '5432' }
$env:ConnectionStrings__Default = "Host=localhost;Port=$port;Database=$db;Username=$user;Password=$($values.POSTGRES_PASSWORD)"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:COOKIE_SECURE = 'false'
$env:APP_PUBLIC_URL = 'http://localhost:5173'

dotnet run --project (Join-Path $root 'backend/src/ApolloData.Api') --launch-profile http
