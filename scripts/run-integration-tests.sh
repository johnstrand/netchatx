#!/usr/bin/env bash
#
# run-integration-tests.sh - Runs containerized XMPP integration tests using Testcontainers
#

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "==> Running live XMPP integration tests (STANZA_INTEGRATION_TESTS=1)..."
export STANZA_INTEGRATION_TESTS=1

dotnet test "$REPO_ROOT/tests/Stanza.IntegrationTests/Stanza.IntegrationTests.csproj" "$@"
