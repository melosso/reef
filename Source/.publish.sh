#!/usr/bin/env bash
set -euo pipefail

# Reef - Publish Orchestrator

# Check required tools
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet is not installed or not in PATH" >&2
    exit 1
fi

SCRIPT_DIR=$(cd "$(dirname "$0")" && pwd)
REPO_BASE=$(cd "$SCRIPT_DIR/.." && pwd)

echo "=== Reef - Full Publish ==="
echo "Repository root: $REPO_BASE"
echo ""

bash "$SCRIPT_DIR/Reef/.publish.sh"

echo ""
echo "=== All components published successfully ==="
