<#
.SYNOPSIS
  Creates .env from .env.example with freshly generated local secrets.
.DESCRIPTION
  Generates random POSTGRES_PASSWORD, ADMIN_PASSWORD and OTP_HASH_KEY. Secrets are written only to
  .env (git-ignored) and are not printed. Email is sent through Gmail: fill in the SMTP values afterwards.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/init-env.ps1
#>
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$example = Join-Path $root '.env.example'
$target = Join-Path $root '.env'

if ((Test-Path $target) -and -not $Force) {
    Write-Host ".env already exists - nothing changed (use -Force to regenerate)." -ForegroundColor Yellow
    exit 0
}

function New-Secret([int]$length) {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $bytes = New-Object byte[] $length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

$content = [IO.File]::ReadAllText($example, [Text.Encoding]::UTF8)
$content = $content -replace '(?m)^POSTGRES_PASSWORD=.*$', "POSTGRES_PASSWORD=$(New-Secret 24)"
$content = $content -replace '(?m)^ADMIN_PASSWORD=.*$', "ADMIN_PASSWORD=$(New-Secret 20)"
$content = $content -replace '(?m)^OTP_HASH_KEY=.*$', "OTP_HASH_KEY=$(New-Secret 40)"

[IO.File]::WriteAllText($target, $content, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Created .env with generated secrets." -ForegroundColor Green
Write-Host "Administrator user name: admin (password: see ADMIN_PASSWORD in .env)"
Write-Host "Email: Gmail - fill in SMTP_USERNAME, SMTP_FROM_EMAIL and SMTP_PASSWORD (App Password) in .env."
Write-Host "Next: docker compose up --build"
