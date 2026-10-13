<#
.SYNOPSIS
  Creates .env from .env.example with freshly generated local secrets.
.DESCRIPTION
  Generates random POSTGRES_PASSWORD, ADMIN_PASSWORD and OTP_HASH_KEY. Secrets are written only to
  .env (git-ignored) and are not printed. With -Mailpit the email settings point to the local Mailpit
  container instead of Gmail, so no real email is sent.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/init-env.ps1 -Mailpit
#>
param(
    [switch]$Mailpit,
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

if ($Mailpit) {
    $content = $content -replace '(?m)^SMTP_(HOST|PORT|USE_STARTTLS|USERNAME|PASSWORD|FROM_EMAIL|FROM_NAME)=.*\r?\n', ''
    $content = $content -replace '(?m)^# (COMPOSE_PROFILES|SMTP_[A-Z_]+|MAILPIT_UI_PORT)=', '$1='
}

[IO.File]::WriteAllText($target, $content, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Created .env with generated secrets." -ForegroundColor Green
Write-Host "Administrator user name: admin (password: see ADMIN_PASSWORD in .env)"
if ($Mailpit) {
    Write-Host "Email: Mailpit - open http://localhost:8025 to read login codes."
} else {
    Write-Host "Email: Gmail - fill in SMTP_USERNAME, SMTP_FROM_EMAIL and SMTP_PASSWORD (App Password) in .env."
}
Write-Host "Next: docker compose up --build"
