# Datadog WebGL Game Monitoring Plan

**Date**: 2026-07-20  
**Version**: 1.0  
**Scope**: NPC System WebGL Client + Unity Dedicated Server + All Backend Services

---

## Executive Summary

This document defines a production-ready monitoring strategy for tracking the NPC Platform WebGL game (browser-based client) across its entire stack:

- **Frontend**: WebGL browser client (Nginx-hosted)
- **Transport**: Netcode WebSocket + REST APIs
- **Authentication**: Supabase Gotrue
- **Dialogue AI**: LocalAI LLM + Qdrant RAG
- **Memory/Graph**: Cognee memory service
- **Infrastructure**: Docker containers, metrics, logs, traces

**Key Goals**:
1. Detect and alert on frontend user-facing issues (404, 5xx, slow loads)
2. Track dialogue AI performance (latency, errors, token usage)
3. Monitor infrastructure health (containers, GPU, database)
4. Correlate WebGL errors with backend failures
5. Provide actionable dashboards for dev + ops teams

---

## 1. Architecture & Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ Browser Client (WebGL)                                          │
│  ├─ datadog-rum-init.js (RUM SDK)                               │
│  │   └─→ Browser events → ddproxy:9090 → Datadog intake        │
│  ├─ fetch() calls to API endpoints                              │
│  └─ Console errors + warnings                                   │
└────────────────────────┬────────────────────────────────────────┘
                         │ HTTPS :8085
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│ Nginx (npc-webgl-client)                                        │
│  ├─ Access + error logs → Docker stdout → dd-agent              │
│  ├─ /nginx_status metrics → dd-agent                            │
│  ├─ Request routing (proxy_pass)                                │
│  └─ HTML injection: datadog-rum-init.js                         │
└────────────────────────┬────────────────────────────────────────┘
        │     │     │     │      │          │
        │     │     │     │      │          └─ /dd-intake → ddproxy:9090
        │     │     │     │      └─ /v1/* → :8080 (LocalAI)
        │     │     │     └─ /rest/* → :8092 (PostgREST)
        │     │     └─ /auth/* → :8091 (Gotrue)
        │     └─ /ws → :11474 (Netcode WebSocket)
        └─ / → :80 (WebGL static files)
        
                    ▼
┌─────────────────────────────────────────────────────────────────┐
│ Dedicated Server (npc-dedicated-server)                         │
│  ├─ NPCFlowLogger → stdout JSON → dd-agent logs                 │
│  ├─ DatadogMetricsService → UDP :8125 (DogStatsD)               │
│  ├─ DatadogTraceService → TCP :8126 (APM traces)                │
│  ├─ Netcode transport (:11474)                                  │
│  └─ LocalAI LLM calls (:8080)                                   │
└────────────────────────┬────────────────────────────────────────┘
        │         │              │
        │         └─ LocalAI :8080
        │              │ HTTP /v1/chat/completions
        │              └─ DogStatsD metrics
        │
        └─ Supabase (:8091, :8092, :8093)
             │ Auth, REST, Realtime
             └─ Datadog logs (Edge Functions)
             
        └─ Qdrant (:6333)
             └─ Vector search metrics
             
        └─ Cognee (:8000)
             └─ Memory API, graph operations
             
        └─ Datadog Agent (dd-agent)
             ├─ Collect all metrics
             ├─ Parse all logs
             ├─ Receive APM traces
             └─ Forward to Datadog SaaS
```

---

## 2. Metrics & Monitoring

### 2.1 Frontend (WebGL Client) — RUM

**Source**: Browser (datadog-rum-init.js)  
**Namespace**: `browser.*` (auto-prefixed)  
**Key Metrics**:

| Metric | Description | Alert Threshold | Type |
|--------|-------------|-----------------|------|
| `browser.load_time` | Page load (DOM ready) | > 5s | Performance |
| `browser.resource.time_to_first_byte` | TTFB from static/API | > 2s | Performance |
| `browser.web_vital.lcp` | Largest Contentful Paint | > 2.5s | Core Web Vital |
| `browser.web_vital.inp` | Interaction to Paint | > 100ms | Core Web Vital |
| `browser.web_vital.cls` | Cumulative Layout Shift | > 0.1 | Core Web Vital |
| `browser.error_count` | JavaScript errors | > 5/min | Error |
| `browser.resource.status_code` | HTTP response codes | 4xx, 5xx spike | HTTP |
| `browser.long_task` | Tasks blocking main thread | > 50ms | Performance |

**Dashboard Widgets**:
- [ ] LCP/INP/CLS trend (last 24h, grouped by page route)
- [ ] Error rate by endpoint (line chart)
- [ ] Resource loading waterfall (top 10 slowest)
- [ ] User sessions with errors (table, sortable)

---

### 2.2 HTTP Transport — Nginx Logs

**Source**: Nginx stdout → dd-agent  
**Service**: `npc-webgl-client`  
**Log Pattern**: Structured JSON (grok-parsed by dd-agent)  
**Key Metrics**:

| Metric | Query | Alert Threshold |
|--------|-------|-----------------|
| 5xx Error Rate | `nginx.upstream.status:5xx` | > 1% (last 5min) |
| 4xx Error Rate | `nginx.upstream.status:4xx` | > 10% |
| Response Time | `http.response_time_ms` avg/p99 | p99 > 2000ms |
| Upstream Errors | `upstream_addr:error` count | > 10 per min |
| WebSocket Connections | `request_path:/ws` | Connection drop > 20% |
| Latency by Endpoint | `request_path` grouped | API /rest > 500ms |

**Sample Alert Query**:
```
avg:nginx.requests{status:5xx,service:npc-webgl-client} > 0.01
```

---

### 2.3 Dialogue System (LocalAI LLM + RAG)

**Source**: Unity DogStatsD + APM traces  
**Namespace**: `llm.*`, `qdrant.*`, `dialogue.*`  
**Key Metrics**:

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `llm.request.duration_ms` | Inference latency (avg/p99) | avg > 5000ms, p99 > 15000ms |
| `llm.request.tokens_generated` | Tokens per response | — |
| `llm.request.errors` | Failed completions | > 5% rate |
| `llm.model.cache_hit_rate` | KV cache hits | < 50% = concern |
| `qdrant.search.duration_ms` | Vector search latency | > 500ms |
| `qdrant.search.results_count` | Results per query | 0 results > 10% |
| `dialogue.turn_latency_ms` | Full dialogue turn (client → server → response) | > 10000ms |
| `dialogue.error_rate` | Failed dialogue responses | > 2% |
| `llm.token_usage` | Cumulative tokens (gauge) | — |

**Sample Span Attributes** (APM):
```
span.tags:
  - llm.model: gpt2-medium
  - llm.tokens_prompt: 100
  - llm.tokens_completion: 50
  - qdrant.collection: npc-knowledge
  - qdrant.query_count: 5
  - dialogue.npc_id: npc-001
  - dialogue.player_id: player-123
  - dialogue.error: (if error)
```

**Dashboard Widgets**:
- [ ] Inference latency trend (p50/p95/p99)
- [ ] Token usage stacked area (by model)
- [ ] Qdrant search latency heatmap
- [ ] Error rate by NPC ID (top 10)
- [ ] Cache hit rate gauge

---

### 2.4 Authentication System (Supabase Gotrue)

**Source**: Auth logs (Supabase Edge Functions → dd-agent)  
**Namespace**: `auth.*`  
**Key Metrics**:

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `auth.login.attempts` | Login attempts (success + failures) | — |
| `auth.login.duration_ms` | Login latency | > 3000ms |
| `auth.login.failures` | Failed logins | > 5/min |
| `auth.login.errors` | Error types (invalid_credentials, network, etc) | > 10% |
| `auth.session.duration_seconds` | Active session length | — |
| `auth.token.refresh_count` | Token refreshes per session | — |
| `auth.token.expiry_errors` | Expired token rejections | — |

**Sample Alert**:
```
sum:auth.login.failures{service:supabase}.as_count() > 5 in last('5m')
```

---

### 2.5 Database & Realtime (Supabase)

**Source**: PostgREST logs + Realtime traces  
**Namespace**: `db.*`, `realtime.*`  
**Key Metrics**:

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `db.query.duration_ms` | Database query latency (avg/p99) | avg > 100ms |
| `db.connections_active` | Active Postgres connections | > 40 (reserve 10 buffer) |
| `db.connection_errors` | Failed connection attempts | > 5/min |
| `realtime.message_count` | WebSocket messages sent | — |
| `realtime.subscriber_count` | Active subscribers | < 1 = concern |
| `realtime.channel_latency_ms` | Message delivery latency | > 500ms |
| `db.row_count` | Table row counts (e.g., dialogue_history) | — |

---

### 2.6 Infrastructure & Containers

**Source**: dd-agent (auto-discovery) + custom monitors  
**Namespace**: `docker.*`, `system.*`  
**Key Metrics**:

| Metric | Description | Alert Threshold |
|--------|-------------|-----------------|
| `docker.cpu.usage` | Container CPU % | > 80% |
| `docker.memory.usage` | Container memory % | > 85% |
| `docker.network.bytes_recv/sent` | Container network I/O | — |
| `system.disk.free` | Disk free (%) | < 10% |
| `system.memory.free` | System memory free (%) | < 5% |
| `system.load.1` | 1-min load average | > 4 (adjust per CPU count) |
| `container.health_status` | Container health check | down/unhealthy |
| `nvidia.gpu.memory_free` | GPU memory free (%) | < 20% |

**Containers to Monitor**:
- dd-agent (Datadog agent)
- npc-dedicated-server (Unity server)
- npc-webgl-client (Nginx)
- localai (LLM inference)
- qdrant (vector DB)
- supabase-gotrue, supabase-rest, supabase-realtime
- cognee-api (memory service)
- postgres (main DB)
- redis (cache, if used)

---

## 3. Log Sources & Parsing

### 3.1 Nginx Access/Error Logs

**File**: `/var/log/nginx/access.log`, `/var/log/nginx/error.log`  
**Parser**: Grok (built-in Datadog pattern)  
**Tags**:
- `service:npc-webgl-client`
- `source:nginx`
- `env:production`

**Extracted Fields** (via grok):
- `http.method`
- `http.status_code`
- `http.request_path`
- `http.response_time_ms`
- `http.client_ip`
- `upstream_addr` (backend server)
- `upstream_status` (backend response)
- `upstream_response_time_ms`

**Alert Query**:
```
service:npc-webgl-client status:error | stats count by http.status_code
```

---

### 3.2 Unity Dedicated Server Logs

**Source**: NPCFlowLogger → stdout (JSON-structured)  
**Parser**: JSON  
**Sample Log**:
```json
{
  "timestamp": "2026-07-20T14:30:45.123Z",
  "stage": "DialogueResponse",
  "status": "success",
  "level": "info",
  "message": "Dialogue turn completed",
  "source": "NPCDialogueManager",
  "player_id": "player-123",
  "npc_id": "npc-001",
  "llm_duration_ms": 1234,
  "qdrant_duration_ms": 45,
  "tags": ["dialogue", "llm", "rag"]
}
```

**Tags**:
- `service:npc-server`
- `source:unity-server`
- `env:production`

**Extracted Fields**:
- `stage` (parsed as event type)
- `status` (parsed as severity)
- `level` (mapped to Datadog log status: error/warning/info/debug)
- `llm_duration_ms`, `qdrant_duration_ms` (metrics)
- `player_id`, `npc_id` (facets for grouping)

**Alert Query**:
```
service:npc-server status:error | stats count as error_count by stage | filter error_count > 5
```

---

### 3.3 LocalAI Logs

**Source**: LocalAI stdout/stderr → dd-agent  
**Parser**: JSON or text  
**Key Fields**:
- `model_name`
- `request_id`
- `duration_ms`
- `error` (if any)
- `tokens` (prompt + completion)

**Alert Query**:
```
service:localai status:error OR message:*timeout* | stats count by model_name
```

---

### 3.4 Qdrant Logs

**Source**: Qdrant stdout → dd-agent  
**Parser**: JSON  
**Key Fields**:
- `operation` (search, upsert, delete)
- `collection_name`
- `duration_ms`
- `result_count`
- `error`

**Alert Query**:
```
service:qdrant operation:search status:error | stats count, avg(duration_ms) by collection_name
```

---

### 3.5 Supabase Edge Functions Logs

**Source**: Edge Functions stderr → dd-agent  
**Service**: `supabase-edge-functions`  
**Key Indicators**:
- Function execution time
- Error stack traces
- Database queries (if logged)

---

## 4. Monitors & Alerts

### 4.1 Existing Monitors (configured)

| ID | Name | Type | Query | Threshold | Notify |
|-------|------|------|-------|-----------|--------|
| 21231976 | LLM Inference Latency High | Metric | `avg:llm.request.duration_ms{*}` | > 5000ms (5min) | — |
| 21231959 | Dialogue Turn Latency High | Metric | `avg:dialogue.turn_latency_ms{*}` | > 10000ms (5min) | — |
| 21231985 | Qdrant RAG Search Errors Spike | Metric | `sum:qdrant.search.errors{*}` | > 5% rate (10min) | — |
| 21231986 | Realtime WebSocket Server Errors | Log | `source:npc-server status:error tag:websocket` | > 10 (5min) | — |

---

### 4.2 New Monitors (to Create)

#### 4.2.1 Frontend Performance

**Name**: [NPC WebGL] Page Load Time High

**Type**: RUM  
**Query**: `avg:browser.load_time{*}`  
**Condition**: > 5 seconds for 5 minutes  
**Severity**: Warning  
**Message**:
```
{{#is_alert}}
  🐢 WebGL client page load is slow!
  
  Current: {{value}}s (threshold: {{threshold}}s)
  
  Check:
  - Nginx upstream response time
  - Asset compression/caching
  - Browser LCP (largest contentful paint)
  
  Dashboard: https://app.datadoghq.com/rum/sessions
{{/is_alert}}

{{#is_recovery}}
  ✅ Page load time normalized
{{/is_recovery}}
```

---

#### 4.2.2 HTTP Errors

**Name**: [NPC WebGL] 5xx Error Rate High

**Type**: Metric  
**Query**: 
```
avg:nginx.upstream.status{status:5xx, service:npc-webgl-client}.as_rate()
```
**Condition**: > 1% (0.01) for 3 minutes  
**Severity**: Critical  
**Message**:
```
{{#is_alert}}
  🚨 WebGL client receiving 5xx errors from backends!
  
  Error Rate: {{value}}% (threshold: {{threshold}}%)
  
  Affected Upstreams:
  {{#each endpoint}}
    - {{endpoint}}: {{status}}
  {{/each}}
  
  Check:
  1. Dedicated server health: docker ps | grep npc-server
  2. LocalAI status: curl http://localhost:8080/v1/models
  3. Supabase status: curl http://localhost:8091/health
  4. Logs: Filter by service:npc-server status:error
  
  Actions:
  - Restart failed container: docker restart <container>
  - Check GPU memory: nvidia-smi
  - Review error logs in Logs Explorer
{{/is_alert}}

{{#is_recovery}}
  ✅ 5xx error rate returned to normal
{{/is_recovery}}
```

**Notify**: Slack #npc-platform-alerts, PagerDuty

---

#### 4.2.3 WebSocket Connection Health

**Name**: [NPC Netcode] WebSocket Connection Drop

**Type**: Log  
**Query**: 
```
service:npc-server tag:websocket status:error
  | stats count as error_count by host
```
**Condition**: `error_count > 20` for 2 minutes  
**Severity**: Critical  
**Message**:
```
{{#is_alert}}
  ⚠️ WebSocket connections dropping on {{host}}!
  
  Errors in 2min: {{value}}
  
  Check:
  - Network connectivity to client
  - Netcode transport config
  - Player state sync
  
  Logs: https://app.datadoghq.com/logs?query=service:npc-server%20tag:websocket%20status:error
{{/is_alert}}
```

---

#### 4.2.4 Auth Failure Rate

**Name**: [NPC Auth] Login Failure Rate High

**Type**: Log  
**Query**: 
```
service:supabase-gotrue status:error
  | stats count as failures
```
**Condition**: `failures > 10` for 5 minutes  
**Severity**: Warning  
**Message**:
```
{{#is_alert}}
  🔓 Auth service experiencing high failure rate!
  
  Failed Logins (5min): {{value}}
  
  Possible causes:
  - Database connection issues
  - JWT signing problems
  - Rate limiting triggered
  
  Dashboard: Check database connections + Supabase logs
{{/is_alert}}
```

---

#### 4.2.5 RAG Search Quality

**Name**: [NPC RAG] Zero Results Rate High

**Type**: Metric  
**Query**: 
```
sum:qdrant.search.results_count{*}.as_count() == 0
  / sum:qdrant.search.queries{*}.as_count()
```
**Condition**: > 10% for 10 minutes  
**Severity**: Warning  
**Message**:
```
{{#is_alert}}
  🔍 RAG search returning no results!
  
  Zero-result queries: {{value}}%
  
  Impact:
  - NPCs may give generic responses
  - Dialogue quality degraded
  
  Actions:
  1. Check Qdrant collection: http://localhost:6333/collections
  2. Verify embeddings: Check embedding model running
  3. Review search queries in logs for anomalies
{{/is_alert}}
```

---

#### 4.2.6 GPU Memory Pressure

**Name**: [Infrastructure] GPU Memory Low

**Type**: Metric  
**Query**: `avg:nvidia.gpu.memory_used{*} / avg:nvidia.gpu.memory_total{*}`  
**Condition**: > 90% for 5 minutes  
**Severity**: Warning  
**Message**:
```
{{#is_alert}}
  📊 GPU memory running low!
  
  Usage: {{value}}% (GPU may OOM)
  
  Running processes:
  - Check: nvidia-smi
  - Likely cause: Large model weights or batch size too high
  
  Actions:
  1. Reduce batch size in LocalAI config
  2. Restart inference service to clear memory
  3. Monitor: gpu_memory_usage dashboard
{{/is_alert}}
```

---

#### 4.2.7 Database Connections

**Name**: [Infrastructure] Database Connection Pool Near Limit

**Type**: Metric  
**Query**: `avg:postgresql.connections{*}`  
**Condition**: > 40 (out of 50) for 5 minutes  
**Severity**: Warning  
**Message**:
```
{{#is_alert}}
  🗄️ Database connection pool near capacity!
  
  Active Connections: {{value}} / 50
  
  Impact:
  - New connections may be rejected
  - Latency may increase
  
  Check:
  1. Long-running queries: SELECT * FROM pg_stat_statements
  2. Idle connections: SELECT * FROM pg_stat_activity WHERE state='idle'
  3. Close idle: SELECT pg_terminate_backend(pid) FROM ...
{{/is_alert}}
```

---

#### 4.2.8 Container Crash Loop

**Name**: [Infrastructure] Container Health Check Failed

**Type**: Log  
**Query**: `container_health_status:unhealthy OR container_status:down`  
**Condition**: Any container down for > 1 minute  
**Severity**: Critical  
**Message**:
```
{{#is_alert}}
  💥 Container {{container_name}} is down/unhealthy!
  
  Status: {{container_status}}
  Last seen: {{timestamp}}
  
  Recovery steps:
  1. Check logs: docker logs {{container_name}}
  2. Restart: docker restart {{container_name}}
  3. Inspect config: docker inspect {{container_name}}
  
  If recurring:
  - Review docker-compose.yml
  - Check resource limits (OOM, CPU throttling)
  - Review startup errors
{{/is_alert}}

{{#is_recovery}}
  ✅ {{container_name}} is healthy again
{{/is_recovery}}
```

**Notify**: Slack #infra-alerts, PagerDuty (critical)

---

## 5. Triggers & Escalation Policy

### 5.1 Trigger Definitions

#### T1 — User-Facing Issue (P1 / Critical)

**Triggers**:
- 5xx error rate > 1% for 3 min
- Page load time > 5s for 5 min
- WebSocket connection drop > 20 per min
- Any critical container down > 1 min

**Escalation**:
1. Slack: #npc-platform-alerts (immediate)
2. PagerDuty: Create incident (P1)
3. On-call engineer: SMS + call
4. Exec notification: If outage > 5 min

**Dashboard Link**: https://app.datadoghq.com/dash/unified/npc-platform-webgl

---

#### T2 — Performance Degradation (P2 / Warning)

**Triggers**:
- LLM inference latency p99 > 8s for 5 min
- Dialogue turn latency > 8s for 5 min
- Database query latency p99 > 500ms for 10 min
- Nginx upstream errors > 5% for 10 min

**Escalation**:
1. Slack: #npc-platform-alerts (notification)
2. On-call engineer: Check within 30 min (optional)
3. Investigation: Review dashboards + logs

**Runbook**: `./Documentation/Runbooks/Latency_Troubleshooting.md`

---

#### T3 — Infrastructure Alert (P2 / Warning)

**Triggers**:
- Container CPU > 80% for 10 min
- Disk free < 10%
- GPU memory > 90% for 5 min
- Database connections > 40 for 5 min
- System load > 4 for 5 min

**Escalation**:
1. Slack: #infra-alerts (notification)
2. Ops team: Review within 1 hour
3. Action: Scale resources or optimize

---

#### T4 — Log Anomaly (P3 / Info)

**Triggers**:
- Error log volume > 3x baseline for 10 min
- Specific error spike (e.g., `timeout`, `OOM`)
- RAG zero-result rate > 10% for 10 min

**Escalation**:
1. Slack: #npc-platform-info (notification)
2. Dev team: Review logs during next standup
3. Action: File GitHub issue if pattern repeats

---

### 5.2 Notification Channels

| Channel | Alert Types | Frequency |
|---------|------------|-----------|
| Slack #npc-platform-alerts | T1 (Critical) | Immediate |
| Slack #npc-platform-info | T3, T4 | Daily digest |
| Slack #infra-alerts | Infrastructure T2, T3 | Immediate (T2), hourly digest (T3) |
| PagerDuty (P1) | T1 + container down | Escalate after 5 min no ack |
| Email (daily digest) | All alerts | 09:00 UTC |

---

## 6. Dashboards (to Create)

### 6.1 WebGL Frontend Overview

**Name**: NPC WebGL — Frontend Performance  
**Refresh**: 30 seconds  
**Widgets**:

1. **LCP/INP/CLS Scorecard** (Top left)
   - RUM Core Web Vitals
   - Last 24h trend

2. **Page Load Waterfall** (Top center)
   - DNS → TCP → TLS → Request → Response → Render
   - Colored bars for each phase
   - Show p50, p95, p99

3. **Error Count by Type** (Top right)
   - Pie chart: JS errors, 4xx, 5xx, network errors
   - Click-through to error details

4. **Upstream Response Time** (Middle left)
   - Line chart: /ws, /auth, /rest, /v1 endpoints
   - Show avg + p99
   - Highlight outliers

5. **5xx Error Rate Timeline** (Middle center)
   - Area chart
   - Grouped by upstream (dedicated server, LocalAI, etc)

6. **User Sessions with Errors** (Middle right)
   - Table: session_id, error_count, last_error_time, client_ip
   - Click to view session replay

7. **Resource Loading (bottom left)**
   - Bar chart: Top 10 slowest assets
   - Grouped by type (bundle.js, image, CSS, API)

8. **Browser Compatibility** (bottom center)
   - Table: Browser type, user count, avg_load_time, error_rate
   - Identify browser-specific issues

9. **Custom Events** (bottom right)
   - Counters for: dialogue_start, dialogue_end, trade_event, error_event
   - Last 1h

---

### 6.2 Backend Performance Dashboard

**Name**: NPC Server — Dialogue & Inference  
**Refresh**: 30 seconds  
**Widgets**:

1. **Dialogue Latency Trend** (Top)
   - Line chart: llm.request.duration_ms avg/p95/p99
   - Stacked breakdown: embedding, search, inference

2. **Inference Model Stats** (Top right)
   - Gauge: Avg tokens per request
   - Gauge: Cache hit rate
   - Gauge: Requests per second

3. **RAG Search Performance** (Middle left)
   - Heatmap: Search latency by collection
   - X-axis: collection_name, Y-axis: time, color: latency

4. **NPC Error Rate** (Middle center)
   - Table: npc_id, error_count (last 1h), error_types
   - Sort by error_count desc

5. **Dialogue Turn Breakdown** (Middle right)
   - Stacked bar: llm_time, qdrant_time, network_time, other
   - Show trend over time

6. **Token Usage** (Bottom)
   - Area chart: cumulative tokens
   - Stacked by model
   - Show rate: tokens/sec

---

### 6.3 Infrastructure Health Dashboard

**Name**: NPC Infrastructure — System Health  
**Refresh**: 60 seconds  
**Widgets**:

1. **Container Status Grid** (Top left)
   - 4x3 grid: Container name, status (green/red), CPU%, mem%
   - Quick restart buttons

2. **GPU Utilization** (Top center)
   - Gauge: GPU memory %
   - Gauge: GPU compute %
   - Gauge: Temperature °C

3. **Disk Usage** (Top right)
   - Gauge: Free space %
   - Bar: Usage by mount point (/,  /var, /mnt)

4. **Database Health** (Middle)
   - Gauge: Active connections
   - Gauge: Query latency p99
   - Gauge: Replication lag (if applicable)
   - Table: Top 5 slow queries

5. **Network I/O** (Bottom left)
   - Line chart: Bytes in/out per container
   - Grouped by container

6. **System Load** (Bottom center)
   - Line chart: Load 1/5/15 min
   - Reference line: CPU core count

7. **Logs Volume** (Bottom right)
   - Bar chart: Log count per service per hour
   - Color by severity (error/warning/info)

---

## 7. Custom Metrics Reference

### 7.1 DogStatsD Metrics (from Unity)

**Emitted by**: `DatadogMetricsService` (Assets/Scripts/Runtime/Monitoring/DatadogMetricsService.cs)

```csharp
// Dialogue metrics
DatadogMetricsService.Gauge("dialogue.turn_latency_ms", latency, tags: new[] { "npc_id:npc-001" });
DatadogMetricsService.Increment("dialogue.turn_count", tags: new[] { "npc_id:npc-001" });
DatadogMetricsService.Increment("dialogue.error", tags: new[] { "error_type:timeout", "npc_id:npc-001" });

// LLM metrics
DatadogMetricsService.Gauge("llm.request.duration_ms", duration, tags: new[] { "model:gpt2", "status:success" });
DatadogMetricsService.Gauge("llm.tokens_generated", tokenCount, tags: new[] { "model:gpt2" });
DatadogMetricsService.Increment("llm.request.errors", tags: new[] { "error_type:timeout" });

// RAG metrics
DatadogMetricsService.Gauge("qdrant.search.duration_ms", latency, tags: new[] { "collection:npc-knowledge" });
DatadogMetricsService.Gauge("qdrant.search.results_count", resultCount, tags: new[] { "collection:npc-knowledge" });

// Network metrics
DatadogMetricsService.Gauge("network.latency_ms", latency, tags: new[] { "endpoint:/ws" });
DatadogMetricsService.Increment("network.message_sent", tags: new[] { "type:dialogue" });
DatadogMetricsService.Increment("network.connection_errors", tags: new[] { "endpoint:/ws" });

// Auth metrics
DatadogMetricsService.Increment("auth.login_attempt", tags: new[] { "status:success" });
DatadogMetricsService.Gauge("auth.session.count", activeCount);
```

**Tag Conventions**:
- `service:npc-server` (added globally)
- `env:production` (added globally)
- `npc_id:<id>` — which NPC
- `player_id:<id>` — which player
- `model:<name>` — LLM model
- `collection:<name>` — Qdrant collection
- `error_type:<type>` — error classification
- `status:<success|error>` — operation result

---

### 7.2 APM Spans (from Unity)

**Emitted by**: `DatadogTraceService` (Assets/Scripts/Runtime/Monitoring/DatadogTraceService.cs)

```csharp
using (var span = DatadogTracer.StartSpan("dialogue.process_turn"))
{
    span.SetTag("dialogue.npc_id", "npc-001");
    span.SetTag("dialogue.player_id", "player-123");
    
    // This span auto-creates child spans for:
    // - dialogue.retrieve_context (Qdrant search)
    // - dialogue.llm_inference (LocalAI request)
    // - dialogue.response_encode (Response serialization)
}
```

**Trace Attributes**:
- `resource_name` = operation (e.g., "dialogue.process_turn")
- `service` = "npc-server"
- `span.kind` = internal
- `span_id`, `trace_id`, `parent_id` = auto
- Custom tags: dialogue.npc_id, dialogue.player_id, llm.model, etc.

**Visualized in**: Datadog APM → Traces → Service: npc-server

---

## 8. Implementation Checklist

- [x] `.mcp.json` created with Datadog MCP server config
- [x] RUM SDK initialized in WebGL (datadog-rum-init.js)
- [x] DogStatsD metrics collection (DatadogMetricsService)
- [x] APM trace collection (DatadogTraceService)
- [x] Nginx log parsing (conf.d/nginx.d/conf.yaml)
- [x] Unity server log parsing (conf.d/unity.d/conf.yaml)
- [ ] Dashboard creation (6 dashboards × 8-9 widgets each)
- [ ] Monitor setup (8 monitors × alert rules + notifications)
- [ ] Runbook creation (latency troubleshooting, container recovery, etc.)
- [ ] SLO definition (uptime %, latency %, error rate %)
- [ ] Agent access validation (all MCP tools functional for agents)
- [ ] Custom metric expansion (item trades, NPC relationship, etc.)

---

## 9. Troubleshooting

### Issue: Agents cannot access Datadog MCP tools

**Root Cause**: Missing `.mcp.json` configuration or credentials not set  
**Fix**:
1. Verify `.mcp.json` exists in project root
2. Check `DD_API_KEY`, `DD_APP_KEY`, `DD_SITE` env vars are set
3. Test manually: `curl -H "DD-API-KEY: $DD_API_KEY" "https://api.us5.datadoghq.com/api/v1/monitor"`
4. Restart VS Code to reload MCP server

### Issue: Metrics not appearing in Datadog

**Root Cause**: DogStatsD client not initialized or hostname/port wrong  
**Fix**:
1. Verify `DatadogMetricsService.Initialize()` called on server startup
2. Check port 8125 is open: `netstat -uln | grep 8125`
3. Verify agent is running: `docker ps | grep dd-agent`
4. Check agent logs: `docker logs dd-agent | grep -i dogstatsd`

### Issue: Logs not being collected

**Root Cause**: Container labels missing or log format not matching parser  
**Fix**:
1. Verify Docker labels: `docker inspect <container> | grep -A10 Labels`
2. Check log format matches grok pattern
3. Test pattern: Use Datadog Logs → Parsing tab
4. Restart agent: `docker restart dd-agent`

---

## 10. Next Actions

1. **Create Dashboards** (follow section 6 templates)
2. **Create Monitors** (follow section 4.2 definitions)
3. **Update Notification Channels** (Slack, PagerDuty)
4. **Test Alert Triggers** (manually trigger monitor conditions)
5. **Create Runbooks** (add to Documentation/Runbooks/)
6. **Team Training** (show devs how to use dashboards + alerts)
7. **Iterate** (collect feedback, refine thresholds, add metrics)

---

## References

- [Datadog RUM Documentation](https://docs.datadoghq.com/real_user_monitoring/)
- [Datadog APM Documentation](https://docs.datadoghq.com/tracing/)
- [Datadog Log Management](https://docs.datadoghq.com/logs/)
- [DogStatsD Protocol](https://docs.datadoghq.com/developers/dogstatsd/)
- [Monitor Configuration API](https://docs.datadoghq.com/api/latest/monitors/)

---

**Created**: 2026-07-20  
**Last Updated**: 2026-07-20  
**Owner**: Platform Observability Team  
**Status**: Draft (Ready for implementation)
