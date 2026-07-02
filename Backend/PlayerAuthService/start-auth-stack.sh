#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd -- "${SCRIPT_DIR}/../.." && pwd)"
LOG_DIR="/tmp/opencode"
LOG_FILE="${LOG_DIR}/player-auth-service.log"

mkdir -p "${LOG_DIR}"

docker compose -f "${SCRIPT_DIR}/docker-compose.yml" up -d
nohup dotnet run --project "${SCRIPT_DIR}/PlayerAuthService.csproj" >"${LOG_FILE}" 2>&1 &

printf 'Auth DB: postgres://postgres:postgres@localhost:5433/unity_linux_llm_auth\n'
printf 'Auth API: http://localhost:5100\n'
printf 'Log: %s\n' "${LOG_FILE}"
