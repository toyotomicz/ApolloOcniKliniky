<#
.SYNOPSIS
  Loads (or removes) optional demo data: ~45 eye clinics across the Czech Republic with history and messages.
.DESCRIPTION
  Demo clinics use the reserved .test domain and never receive email. Real registrations are not touched.
.EXAMPLE
  powershell -File scripts/seed-demo.ps1            # load demo data (once)
  powershell -File scripts/seed-demo.ps1 -Reset     # recreate it
  powershell -File scripts/seed-demo.ps1 -Remove    # delete demo data
#>
param(
    [switch]$Reset,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$arguments = @('compose', 'run', '--rm', 'api', 'seed-demo-data')
if ($Reset) { $arguments += '--reset' }
if ($Remove) { $arguments += '--remove' }
& docker @arguments
