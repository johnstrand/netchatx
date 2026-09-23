param (
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

Write-Host "==> Running Stanza.Gui ($Configuration)..." -ForegroundColor Cyan
dotnet run --project "$RepoRoot\src\Stanza.Gui\Stanza.Gui.csproj" --configuration $Configuration

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to run Stanza.Gui!" -ForegroundColor Red
    exit $LASTEXITCODE
}
