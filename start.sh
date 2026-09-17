#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

echo ""
echo "=============================================================="
echo "  ✉️  mailStripper  (/meɪl ˈstrɪp.ər/)"
echo "  Strips emails down to one-sentence titles & clean attachments"
echo "=============================================================="

# Check for .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo "❌ Error: The .NET SDK was not found on your system."
    echo "   Please download and install .NET 10 from:"
    echo "   👉 https://dotnet.microsoft.com/download"
    echo ""
    exit 1
fi

PORT=5001
URL="http://localhost:${PORT}"

# Check if port 5001 is already running
if curl -s "${URL}/api/strip/status" > /dev/null 2>&1; then
    echo "✅ mailStripper is already running on ${URL}!"
    echo "🚀 Opening your browser..."
    if command -v open &> /dev/null; then
        open "${URL}" || true
    elif command -v xdg-open &> /dev/null; then
        xdg-open "${URL}" || true
    fi
    exit 0
fi

# Launch mailStripper
echo "⚡ Starting mailStripper on ${URL}..."
dotnet run --project MailStripper/MailStripper.csproj --urls "${URL}" &
SERVER_PID=$!

cleanup() {
    echo ""
    echo "🛑 Stopping mailStripper (PID ${SERVER_PID})..."
    kill "${SERVER_PID}" 2>/dev/null || true
    exit 0
}
trap cleanup INT TERM EXIT

# Wait for server readiness
for i in {1..30}; do
    if curl -s "${URL}/api/strip/status" > /dev/null 2>&1; then
        break
    fi
    sleep 0.5
done

echo ""
echo "=============================================================="
echo "  🎉 mailStripper is LIVE at: ${URL}"
echo "  🔒 100% Local-Only & Air-Gapped (Zero Telemetry, Volatile RAM)"
echo "  ⚡ Powered by MimeKit (Jeffrey Stedfast)"
echo "=============================================================="
echo "  Press Ctrl+C to stop the server at any time."
echo "=============================================================="
echo ""

# Open default browser automatically
if command -v open &> /dev/null; then
    open "${URL}" || true
elif command -v xdg-open &> /dev/null; then
    xdg-open "${URL}" || true
fi

# Keep script running while server is alive
wait "${SERVER_PID}"
