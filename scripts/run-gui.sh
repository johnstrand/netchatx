#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

CONFIGURATION="${1:-Debug}"

echo "==> Running NetChatx.Gui ($CONFIGURATION)..."
dotnet run --project "$REPO_ROOT/src/NetChatx.Gui/NetChatx.Gui.csproj" --configuration "$CONFIGURATION"
