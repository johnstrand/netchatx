<#
.SYNOPSIS
    Manages the local XMPP development Docker environment (Prosody).

.DESCRIPTION
    Spins up, stops, resets, or checks the status of the containerized Prosody XMPP server
    pre-configured with domain 'localhost' and seeded users (usera, userb, userc).

.PARAMETER Action
    The action to perform: start, stop, restart, reset, status, logs. Default is 'start'.

.EXAMPLE
    pwsh ./scripts/dev-xmpp.ps1 start
    pwsh ./scripts/dev-xmpp.ps1 status
    pwsh ./scripts/dev-xmpp.ps1 reset
    pwsh ./scripts/dev-xmpp.ps1 stop
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("start", "stop", "restart", "reset", "status", "logs")]
    [string]$Action = "start"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$composeFile = Join-Path $repoRoot "docker\xmpp\docker-compose.yml"

if (-not (Test-Path $composeFile)) {
    Write-Error "Could not locate docker-compose.yml at '$composeFile'"
    exit 1
}

# Ensure docker CLI is installed and responsive
try {
    $null = docker info 2>&1
} catch {
    Write-Error "Docker does not appear to be running or installed. Please start Docker Desktop first."
    exit 1
}

function Show-ConnectionInfo {
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "       Stanza Local XMPP Development Server (Prosody)     " -ForegroundColor Cyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "  Domain:       " -NoNewline; Write-Host "localhost" -ForegroundColor Yellow
    Write-Host "  c2s Port:     " -NoNewline; Write-Host "5222 (STARTTLS)" -ForegroundColor Green
    Write-Host "  c2s Direct:   " -NoNewline; Write-Host "5223 (Direct TLS)" -ForegroundColor Green
    Write-Host "  HTTP Upload:  " -NoNewline; Write-Host "5280 (HTTP / BOSH)" -ForegroundColor Green
    Write-Host "  Conference:   " -NoNewline; Write-Host "conference.localhost" -ForegroundColor Green
    Write-Host "----------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  Pre-Seeded Test Accounts (Mutual Contacts):" -ForegroundColor White
    Write-Host "    - User A:   " -NoNewline; Write-Host "usera@localhost" -ForegroundColor Yellow; Write-Host " / password: " -NoNewline; Write-Host "password" -ForegroundColor Gray
    Write-Host "    - User B:   " -NoNewline; Write-Host "userb@localhost" -ForegroundColor Yellow; Write-Host " / password: " -NoNewline; Write-Host "password" -ForegroundColor Gray
    Write-Host "    - User C:   " -NoNewline; Write-Host "userc@localhost" -ForegroundColor Yellow; Write-Host " / password: " -NoNewline; Write-Host "password" -ForegroundColor Gray
    Write-Host "  Initial Chat: " -NoNewline; Write-Host "Sample messages pre-seeded between User B -> User A" -ForegroundColor DarkCyan
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host ""
}

switch ($Action.ToLower()) {
    "start" {
        Write-Host "Starting Stanza XMPP dev container..." -ForegroundColor Cyan
        docker compose -f $composeFile up -d --build
        Write-Host "Container started successfully." -ForegroundColor Green
        Show-ConnectionInfo
    }
    "stop" {
        Write-Host "Stopping Stanza XMPP dev container..." -ForegroundColor Cyan
        docker compose -f $composeFile stop
        Write-Host "Container stopped." -ForegroundColor Yellow
    }
    "restart" {
        Write-Host "Restarting Stanza XMPP dev container..." -ForegroundColor Cyan
        docker compose -f $composeFile restart
        Write-Host "Container restarted." -ForegroundColor Green
        Show-ConnectionInfo
    }
    "reset" {
        Write-Host "Resetting Stanza XMPP dev container and wiping data volumes..." -ForegroundColor Yellow
        docker compose -f $composeFile down -v
        Write-Host "Rebuilding and re-seeding clean state..." -ForegroundColor Cyan
        docker compose -f $composeFile up -d --build
        Write-Host "Container reset and re-seeded successfully." -ForegroundColor Green
        Show-ConnectionInfo
    }
    "status" {
        docker compose -f $composeFile ps
        Show-ConnectionInfo
    }
    "logs" {
        docker compose -f $composeFile logs -f
    }
}
