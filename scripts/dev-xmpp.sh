#!/usr/bin/env bash
#
# dev-xmpp.sh - Manages the local XMPP development Docker environment (Prosody)
#
# Usage:
#   ./scripts/dev-xmpp.sh [start|stop|restart|reset|status|logs]
#

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$REPO_ROOT/docker/xmpp/docker-compose.yml"
ACTION="${1:-start}"

if [ ! -f "$COMPOSE_FILE" ]; then
    echo "Error: Could not locate docker-compose.yml at '$COMPOSE_FILE'" >&2
    exit 1
fi

if ! command -v docker &> /dev/null; then
    echo "Error: docker command not found. Please install Docker." >&2
    exit 1
fi

show_connection_info() {
    echo ""
    echo "=========================================================="
    echo "       Stanza Local XMPP Development Server (Prosody)     "
    echo "=========================================================="
    echo "  Domain:       localhost"
    echo "  c2s Port:     5222 (STARTTLS)"
    echo "  c2s Direct:   5223 (Direct TLS)"
    echo "  HTTP Upload:  5280 (HTTP / BOSH)"
    echo "  Conference:   conference.localhost"
    echo "----------------------------------------------------------"
    echo "  Pre-Seeded Test Accounts (Mutual Contacts):"
    echo "    - User A:   usera@localhost / password: password"
    echo "    - User B:   userb@localhost / password: password"
    echo "    - User C:   userc@localhost / password: password"
    echo "  Initial Chat: Sample messages pre-seeded between User B -> User A"
    echo "=========================================================="
    echo ""
}

case "$ACTION" in
    start)
        echo "Starting Stanza XMPP dev container..."
        docker compose -f "$COMPOSE_FILE" up -d --build
        echo "Container started successfully."
        show_connection_info
        ;;
    stop)
        echo "Stopping Stanza XMPP dev container..."
        docker compose -f "$COMPOSE_FILE" stop
        echo "Container stopped."
        ;;
    restart)
        echo "Restarting Stanza XMPP dev container..."
        docker compose -f "$COMPOSE_FILE" restart
        echo "Container restarted."
        show_connection_info
        ;;
    reset)
        echo "Resetting Stanza XMPP dev container and wiping data volumes..."
        docker compose -f "$COMPOSE_FILE" down -v
        echo "Rebuilding and re-seeding clean state..."
        docker compose -f "$COMPOSE_FILE" up -d --build
        echo "Container reset and re-seeded successfully."
        show_connection_info
        ;;
    status)
        docker compose -f "$COMPOSE_FILE" ps
        show_connection_info
        ;;
    logs)
        docker compose -f "$COMPOSE_FILE" logs -f
        ;;
    *)
        echo "Usage: $0 {start|stop|restart|reset|status|logs}" >&2
        exit 1
        ;;
esac
