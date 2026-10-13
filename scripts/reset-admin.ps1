<#
.SYNOPSIS
  Creates or resets the administrator account defined by ADMIN_USERNAME / ADMIN_PASSWORD in .env.
.DESCRIPTION
  Runs the API's maintenance command in a one-off container (there is no HTTP endpoint for this).
  Clears a lockout and revokes the administrator's existing sessions.
  Edit ADMIN_PASSWORD in .env first (at least 12 characters).
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
docker compose run --rm api reset-admin-password
