#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

CONFIGURATION="${1:-Debug}"

echo "==> Running Stanza.Gui ($CONFIGURATION)..."
dotnet run --project "$REPO_ROOT/src/Stanza.Gui/Stanza.Gui.csproj" --configuration "$CONFIGURATION"
