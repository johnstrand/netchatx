param (
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

Write-Host "==> Running NetChatx.Gui ($Configuration)..." -ForegroundColor Cyan
dotnet run --project "$RepoRoot\src\NetChatx.Gui\NetChatx.Gui.csproj" --configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to run NetChatx.Gui!" -ForegroundColor Red
    exit $LASTEXITCODE
}
