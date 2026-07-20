#!/bin/bash
# Entrypoint for the NPC dedicated server container.
# Forwards signals to the Unity process so Docker stop works cleanly.
set -e

SERVER_PORT="${SERVER_PORT:-11474}"
SERVER_ADDRESS="${SERVER_ADDRESS:-0.0.0.0}"

# Locate the server binary — try common names (in /server, lowercase)
if [ -f "/server/LinuxDedicatedServer.x86_64" ]; then
    SERVER_BINARY="/server/LinuxDedicatedServer.x86_64"
elif [ -f "/server/Server1.x86_64" ]; then
    SERVER_BINARY="/server/Server1.x86_64"
elif [ -f "/server/NPCServer.x86_64" ]; then
    SERVER_BINARY="/server/NPCServer.x86_64"
elif [ -f "/server/Server.x86_64" ]; then
    SERVER_BINARY="/server/Server.x86_64"
elif [ -f "/server/Linux.x86_64" ]; then
    SERVER_BINARY="/server/Linux.x86_64"
elif ls /server/*.x86_64 1>/dev/null 2>&1; then
    SERVER_BINARY="$(ls /server/*.x86_64 | head -n 1)"
else
    echo "ERROR: No server binary found in /server/"
    echo "Bind-mount or COPY your Unity dedicated server build to /server/."
    echo "Expected files: LinuxDedicatedServer.x86_64 (or Server1.x86_64 or NPCServer.x86_64), UnityPlayer.so, GameAssembly.so, and the matching *_Data/ folder"
    ls -la /server/ 2>/dev/null || echo "  /server/ is empty or missing"
    exit 1
fi

USE_WEBSOCKETS="${USE_WEBSOCKETS:-false}"
EXTRA_ARGS=""
if [ "$USE_WEBSOCKETS" = "true" ]; then
    EXTRA_ARGS="-npc-websockets"
fi

echo "=== NPC Dedicated Server Container ==="
echo "Binary:     $SERVER_BINARY"
echo "Port:       $SERVER_PORT"
echo "Address:    $SERVER_ADDRESS"
echo "WebSockets: $USE_WEBSOCKETS"
echo "LocalAI:    localhost:8080 (via host networking)"
echo "======================================"

# Start server in background so we can trap signals.
# -batchmode is an Editor-only flag; remove for built dedicated server player.
# -nographics disables graphics initialization (valid for built players).
# Unknown args are passed through to the game code.
"$SERVER_BINARY" \
    -nographics \
    -npc-server \
    -port "$SERVER_PORT" \
    -address "$SERVER_ADDRESS" \
    $EXTRA_ARGS \
    -logFile /dev/stdout &
SERVER_PID=$!

# Forward shutdown signals to Unity process
trap 'echo "Shutting down server (PID $SERVER_PID)..."; kill -TERM $SERVER_PID 2>/dev/null; wait $SERVER_PID; exit 0' SIGTERM SIGINT

wait $SERVER_PID
EXIT_CODE=$?
echo "Server exited with code $EXIT_CODE"
exit $EXIT_CODE
