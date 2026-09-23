param (
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

Write-Host "==> Running tests with code coverage ($Configuration)..." -ForegroundColor Cyan
dotnet test Stanza.slnx --configuration $Configuration --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults

if ($LASTEXITCODE -ne 0) {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "==> Generating coverage reports..." -ForegroundColor Cyan
if (Get-Command reportgenerator -ErrorAction SilentlyContinue) {
    reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coveragereport "-reporttypes:MarkdownSummaryGithub;Html;Cobertura;Badges"
    Write-Host "==> Coverage report generated at coveragereport/index.html" -ForegroundColor Green
    Write-Host "==> GitHub summary markdown at coveragereport/SummaryGithub.md" -ForegroundColor Green
    Write-Host "==> Consolidated Cobertura XML at coveragereport/Cobertura.xml" -ForegroundColor Green
} else {
    Write-Host "reportgenerator tool not found. Install it globally with:" -ForegroundColor Yellow
    Write-Host "  dotnet tool install -g dotnet-reportgenerator-globaltool" -ForegroundColor Yellow
}
