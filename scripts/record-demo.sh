#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

echo "=========================================================="
echo "  mailStripper - Automated Playwright Video Recorder"
echo "=========================================================="

if ! command -v node &> /dev/null; then
    echo "Error: Node.js is required to record video demos."
    exit 1
fi

# Ensure server is running on http://localhost:5001
if ! curl -s http://localhost:5001/api/strip/status > /dev/null; then
    echo "Starting mailStripper on http://localhost:5001..."
    dotnet run --project MailStripper/MailStripper.csproj --urls "http://localhost:5001" &
    SERVER_PID=$!
    
    cleanup() {
        echo "Shutting down temporary server (PID $SERVER_PID)..."
        kill $SERVER_PID 2>/dev/null || true
    }
    trap cleanup EXIT
    
    for i in {1..20}; do
        if curl -s http://localhost:5001/api/strip/status > /dev/null; then
            break
        fi
        sleep 1
    done
fi

# Run Playwright recording script
node scripts/record-demo.js

echo "Recording complete: $REPO_ROOT/demo.mp4"
