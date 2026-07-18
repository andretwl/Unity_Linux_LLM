#!/bin/bash
set -eu

# ─────────────────────────────────────────────────────────────────
# Unity Linux LLM — Container Stack Recomposer with Log Monitoring
# ─────────────────────────────────────────────────────────────────
# Recomposes all backend services in proper order and monitors logs
# for gaps/issues.
#
# Usage:
#   ./scripts/recompose-stack.sh [--all]
#     --all: Start all services (including dev tools)
#     default: Start core services only (qdrant, localai, supabase, datadog)
#
# Outputs:
#   1. Container startup status
#   2. Real-time log tail (filtered by severity)
#   3. Health check results
# ─────────────────────────────────────────────────────────────────

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$PROJECT_ROOT"

# Configuration
INCLUDE_ALL_SERVICES=${1:-}
LOG_DIR="Logs/docker-stack"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# ─────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────

log_info() {
    echo -e "${BLUE}[INFO]${NC} $*"
}

log_ok() {
    echo -e "${GREEN}[OK]${NC} $*"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $*"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $*"
}

# ─────────────────────────────────────────────────────────────────
# Phase 1: Cleanup & Preparation
# ─────────────────────────────────────────────────────────────────

log_info "=== PHASE 1: Cleanup & Preparation ==="

# Create log directory
mkdir -p "$LOG_DIR"
log_info "Logging to: $LOG_DIR"

# Check Docker daemon
if ! docker ps &>/dev/null; then
    log_error "Docker daemon not running. Start Docker first."
    exit 1
fi
log_ok "Docker daemon healthy"

# Check .env file
if [ ! -f Backend/.env ]; then
    log_warn "Backend/.env not found. Copying from template..."
    if [ -f Backend/.env.example ]; then
        cp Backend/.env.example Backend/.env
        log_warn "Please fill in Backend/.env with actual secrets (DD_API_KEY, DD_APP_KEY, etc.)"
    fi
fi

# Stop all existing containers gracefully
log_info "Stopping existing containers (if any)..."
docker compose \
    -f Backend/qdrant/docker-compose.yml \
    -f Backend/localai-proxy/docker-compose.yml \
    -f Backend/supabase-stack/docker-compose.yml \
    -f Backend/datadog-host/docker-compose.yml \
    down --remove-orphans 2>&1 | grep -E "Stopping|Removing" || true

# Also stop other services if --all requested
if [ "$INCLUDE_ALL_SERVICES" = "--all" ]; then
    log_info "Also stopping dev/game services..."
    docker compose \
        -f Backend/codebase-watchdog/docker-compose.yml \
        -f Backend/unity-dedicated-server/docker-compose.yml \
        -f Backend/webgl-client/docker-compose.yml \
        down --remove-orphans 2>&1 | grep -E "Stopping|Removing" || true
fi

sleep 2
log_ok "Cleanup complete"

# ─────────────────────────────────────────────────────────────────
# Phase 2: Start Core Services (with health checks)
# ─────────────────────────────────────────────────────────────────

log_info ""
log_info "=== PHASE 2: Starting Core Services ==="

# Service startup order (dependency-based)
declare -A SERVICES=(
    [Qdrant]="Backend/qdrant"
    [LocalAI\ Proxy]="Backend/localai-proxy"
    [Supabase\ Stack]="Backend/supabase-stack"
    [Datadog\ Agent]="Backend/datadog-host"
)

for service_name in "${!SERVICES[@]}"; do
    service_path="${SERVICES[$service_name]}"
    compose_file="$service_path/docker-compose.yml"
    
    if [ ! -f "$compose_file" ]; then
        log_warn "Skipping $service_name (file not found: $compose_file)"
        continue
    fi
    
    log_info "Starting: $service_name..."
    
    if docker compose -f "$compose_file" up -d 2>&1 | tee -a "$LOG_DIR/startup_${TIMESTAMP}.log" | grep -qE "created|started|already|Started"; then
        sleep 3  # Brief wait for service initialization
        log_ok "$service_name started"
    else
        log_warn "$service_name may have issues (check logs)"
    fi
done

# ─────────────────────────────────────────────────────────────────
# Phase 3: Start Optional Services (if --all requested)
# ─────────────────────────────────────────────────────────────────

if [ "$INCLUDE_ALL_SERVICES" = "--all" ]; then
    log_info ""
    log_info "=== PHASE 3: Starting Optional Services ==="
    
    declare -A OPTIONAL_SERVICES=(
        [Codebase\ Watchdog]="Backend/codebase-watchdog"
        [Dedicated\ Server]="Backend/unity-dedicated-server"
        [WebGL\ Client]="Backend/webgl-client"
    )
    
    for service_name in "${!OPTIONAL_SERVICES[@]}"; do
        service_path="${OPTIONAL_SERVICES[$service_name]}"
        compose_file="$service_path/docker-compose.yml"
        
        if [ -f "$compose_file" ]; then
            log_info "Starting: $service_name..."
            docker compose -f "$compose_file" up -d 2>&1 | tee -a "$LOG_DIR/startup_${TIMESTAMP}.log" || log_warn "$service_name failed to start"
        fi
    done
fi

# ─────────────────────────────────────────────────────────────────
# Phase 4: Health Checks
# ─────────────────────────────────────────────────────────────────

log_info ""
log_info "=== PHASE 4: Health Checks ==="

HEALTH_CHECKS=(
    "Qdrant|http://localhost:6333/health|6333"
    "LocalAI Proxy|http://localhost:8090/health|8090"
    "Supabase Auth|http://localhost:8091/health|8091"
    "Supabase REST|http://localhost:8092/health|8092"
)

for check in "${HEALTH_CHECKS[@]}"; do
    IFS='|' read -r service_name url port <<< "$check"
    
    printf "  %-25s " "$service_name"
    
    for attempt in {1..15}; do
        if curl -sf "$url" >/dev/null 2>&1; then
            echo -e "${GREEN}OK${NC} (attempt $attempt)"
            break
        elif [ $attempt -eq 15 ]; then
            echo -e "${RED}FAIL${NC} (port $port unreachable)"
        else
            echo -n "."
            sleep 2
        fi
    done
done

# ─────────────────────────────────────────────────────────────────
# Phase 5: Status Report
# ─────────────────────────────────────────────────────────────────

log_info ""
log_info "=== PHASE 5: Container Status ==="

docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" | \
    grep -E "qdrant|localai|supabase|datadog|npc-|codebase" || true

# ─────────────────────────────────────────────────────────────────
# Phase 6: Real-Time Log Monitoring
# ─────────────────────────────────────────────────────────────────

log_info ""
log_info "=== PHASE 6: Real-Time Log Monitoring ==="
log_info "Tailing logs (press Ctrl+C to stop)..."
log_info "Filtering for: ERROR, WARNING, WARN, FAIL, exception, panic"
log_info ""

# Create unified log monitor with filtering
(
    # Qdrant
    docker logs -f qdrant 2>&1 | sed 's/^/[qdrant] /' &
    
    # LocalAI
    docker logs -f localai-proxy 2>&1 | sed 's/^/[localai] /' &
    
    # Supabase components
    docker logs -f supabase-stack-auth 2>&1 | sed 's/^/[supabase-auth] /' &
    docker logs -f supabase-stack-db 2>&1 | sed 's/^/[supabase-db] /' &
    docker logs -f supabase-stack-rest 2>&1 | sed 's/^/[supabase-rest] /' &
    
    # Datadog
    docker logs -f dd-agent 2>&1 | sed 's/^/[datadog] /' &
    
    wait
) | tee "$LOG_DIR/unified_${TIMESTAMP}.log" | \
    grep -iE "ERROR|WARN|FAIL|exception|panic|critical|fatal|problem|issue|gap" | \
    tee "$LOG_DIR/errors_${TIMESTAMP}.log"

log_info ""
log_info "=== Log Monitoring Complete ==="
log_info "Full logs saved to:"
log_info "  - $LOG_DIR/startup_${TIMESTAMP}.log (startup output)"
log_info "  - $LOG_DIR/unified_${TIMESTAMP}.log (all container logs)"
log_info "  - $LOG_DIR/errors_${TIMESTAMP}.log (filtered errors/warnings)"

log_ok "Stack recomposition finished!"
