#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Debug}"

echo "==> Running tests with code coverage ($CONFIGURATION)..."
dotnet test Stanza.slnx --configuration "$CONFIGURATION" --settings coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults

echo "==> Generating coverage reports..."
if command -v reportgenerator &> /dev/null; then
    reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coveragereport "-reporttypes:MarkdownSummaryGithub;Html;Cobertura;Badges"
    echo "==> Coverage report generated at coveragereport/index.html"
    echo "==> GitHub summary markdown at coveragereport/SummaryGithub.md"
    echo "==> Consolidated Cobertura XML at coveragereport/Cobertura.xml"
else
    echo "reportgenerator tool not found. Install it globally with:"
    echo "  dotnet tool install -g dotnet-reportgenerator-globaltool"
fi
