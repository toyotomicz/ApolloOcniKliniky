<#
.SYNOPSIS
  Turns off two-factor authentication of the administrator ADMIN_USERNAME (e.g. after losing the phone).
.DESCRIPTION
  Local maintenance command (not available over HTTP). Also revokes the administrator's sessions.
  The administrator can then sign in with the password and set up the authenticator app again.
#>
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
docker compose run --rm api reset-admin-2fa
