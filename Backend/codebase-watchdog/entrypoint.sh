#!/bin/bash
# Entrypoint for the CodebaseEmbedder watchdog container.
# Validates environment and starts `codebase-embedder watch`.
set -e

# Environment defaults
QDRANT_URL="${QDRANT_URL:-http://localhost:6333}"
COLLECTION_NAME="${COLLECTION_NAME:-unity_linux_llm_codebase_v2}"
LOCALAI_BASE_URL="${LOCALAI_BASE_URL:-http://localhost:8080/v1}"
EMBEDDING_MODEL="${EMBEDDING_MODEL:-nomic-embed-text-v1.5}"
PROJECT_SLUG="${PROJECT_SLUG:-Unity_Linux_LLM}"
WATCH_DEBOUNCE_SECONDS="${WATCH_DEBOUNCE_SECONDS:-1.5}"

echo "=== Codebase Watcher Container ==="
echo "Root:         /workspace"
echo "Collection:   $COLLECTION_NAME"
echo "Qdrant:       $QDRANT_URL"
echo "LocalAI:      $LOCALAI_BASE_URL"
echo "Model:        $EMBEDDING_MODEL"
echo "Debounce:     ${WATCH_DEBOUNCE_SECONDS}s"
echo "=================================="

# Verify dependencies are installed
if ! command -v codebase-embedder &>/dev/null; then
    echo "ERROR: codebase-embedder CLI not found in PATH."
    echo "Ensure the Python virtualenv is activated or the package is installed."
    exit 1
fi

# Verify the workspace contains a Unity project (look for Assets/ directory)
if [ ! -d "/workspace/Assets" ]; then
    echo "WARNING: /workspace/Assets not found."
    echo "Bind-mount a Unity project root to /workspace."
    echo "Proceeding in 5s anyway..."
    sleep 5
fi

# Verify Qdrant is reachable
echo "Checking Qdrant at $QDRANT_URL..."
if command -v curl &>/dev/null; then
    if curl -sf "$QDRANT_URL/collections" > /dev/null 2>&1; then
        echo "Qdrant is reachable."
    else
        echo "WARNING: Qdrant not reachable at $QDRANT_URL. Retrying in background..."
    fi
fi

# Start the watchdog with Datadog APM tracing
export DD_SERVICE="${DD_SERVICE:-codebase-watchdog}"
export DD_ENV="${DD_ENV:-production}"
export DD_SITE="${DD_SITE:-us5.datadoghq.com}"
export DD_AGENT_HOST="${DD_AGENT_HOST:-localhost}"
export DD_TRACE_AGENT_PORT="${DD_TRACE_AGENT_PORT:-8126}"

ddtrace-run codebase-embedder watch \
    --root /workspace \
    --qdrant-url "$QDRANT_URL" \
    --collection "$COLLECTION_NAME" \
    --localai-base-url "$LOCALAI_BASE_URL" \
    --embedding-model "$EMBEDDING_MODEL" \
    --project-slug "$PROJECT_SLUG" \
    --debounce "$WATCH_DEBOUNCE_SECONDS"
