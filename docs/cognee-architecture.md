# Cognee Architecture for Unity NPC LLM

## Overview

Cognee is now treated as a Hermes CLI agent-memory backend first, not as the primary codebase-retrieval path for this repository.

Current role split:
- Qdrant = codebase retrieval / structural code RAG
- Hermes built-in memory + Cognee plugin = agent memory surfaced through the Hermes CLI
- Unity `CogneeMemoryService` = present in the scene, but currently disabled and not part of the preferred runtime path

The service still runs separately on port 8000 with an all-Postgres backend (relational + pgvector + graph) alongside LocalAI on port 8080.

## Relationship to Qdrant

| Layer | Qdrant (6333) | Cognee (8000) |
|-------|---------------|---------------|
| Content | Codebase index (enriched chunks) | Hermes agent memories + optional NPC dialogue history |
| Schema | Versioned collections with structured payloads | Datasets with pgvector + optional graph |
| Source | CodebaseEmbedder pipeline | Runtime text and Hermes memory writes |
| Update cadence | Manual re-index | Continuous agent writes through Hermes |
| Query pattern | C#/Python SDK for codebase RAG | Hermes CLI memory provider + direct HTTP diagnostics |

Cognee has no access to Qdrant. They are completely independent systems sharing nothing but the same VM.

## Effective Data Flow

```
                  ┌──────────────────────────────┐
                  │ Hermes CLI / built-in memory │
                  └──────────────┬───────────────┘
                                 │
                                 │ memory writes
                                 ▼
┌──────────────────────────────────────────────────────────┐
│              Cognee API (localhost:8000)                │
│                                                          │
│  Hermes Cognee plugin / runner drives remember/recall   │
│  wrappers for agent memory operations                    │
│                          │                               │
│                          ▼                               │
│  embed with nomic-embed-text-v1.5 (768d) via LocalAI    │
│                          │                               │
│                          ▼                               │
│  COGNEE_SKIP_SUMMARIZATION=true                          │
│  (graph extraction disabled for this lane)              │
│                          │                               │
│                          ▼                               │
│                  store in Postgres + pgvector            │
└──────────────────────────────────────────────────────────┘
```

## Current State (as of 2026-07-01)

### Running
- Cognee API v1.2.2-local on port 8000 (gunicorn + uvicorn)
- All-Postgres backend (relational DB, pgvector, graph DB)
- `COGNEE_SKIP_SUMMARIZATION=true` (no entity/graph extraction)
- Hermes memory provider reports `cognee` as active via `hermes memory status`
- `nomic-embed-text-v1.5` via LocalAI for embeddings
- Hermes Cognee plugin/runner under `~/.hermes/plugins/cognee/` connects to :8000

### Datasets
- Various `hermes_project_*` datasets — agent conversation memories
- No codebase-reference dataset is currently required for normal CLI agent memory use

### Not running
- No graph/KG extraction (intentionally disabled)
- No summarization (intentionally disabled)
- `CogneeMemoryService` in the Unity scene is inactive
- Cognee MCP server is not configured

## KG / Graph Decision

Recommendation: skip the graph entirely for this workspace lane.

Why:
1. LLM-based entity extraction on code content has been noisy and not worth the complexity here.
2. The graph adds latency without improving the CLI agent-memory experience you actually care about.
3. With `COGNEE_SKIP_SUMMARIZATION=true`, the stack stays simpler and faster.
4. If structured code relationships are needed, CodebaseEmbedder + Qdrant already provide that more reliably than Cognee extraction.

## Hermes Agent Integration

### What matters operationally
Hermes uses Cognee through the memory provider/plugin path. The practical supported flow is:
1. Hermes built-in memory stores a durable fact
2. The Cognee provider mirrors that into fast-memory files and its plugin-managed deep store
3. A fresh `hermes chat` invocation can recall that memory from the CLI

### Verified on this machine
Observed after cleanup:
- `hermes memory status` reports provider `cognee` as active and available
- a fresh CLI process successfully recalled a disposable memory marker written by an earlier CLI process

### Important limitation
Lower-level raw Cognee HTTP recall/search behavior was inconsistent during direct testing, so the validation target for this workspace is the Hermes CLI memory experience, not arbitrary ad hoc `/api/v1/recall` dataset experiments.

## Codebase Reference Bridge

Location: `Tools/CodebaseEmbedder/scripts/cognee_bridge.py`

Status:
- diagnostic / optional only
- not required for everyday Hermes CLI memory
- kept only as a compact references-only experiment path

Usage:
```bash
cd Tools/CodebaseEmbedder
uv run python3 scripts/cognee_bridge.py \
  --root ../.. \
  --dataset unity_npc_llm_references \
  --force
```

The current bridge reads `.codebase-index/relations.jsonl`, keeps only high-signal structural references, builds compact per-file documents, and sends them through the faster `/api/v1/memify` path. It is useful only for controlled experiments; it is not the primary memory path anymore.

## Unity Runtime Status

`CogneeMemoryService` exists as scene/editor infrastructure, but it is not part of the active recommended runtime lane right now.

Current preference:
- stable LocalAI dialogue first
- local `.rag` / Qdrant where needed for retrieval
- Hermes CLI memory can continue using Cognee
- Unity-side Cognee runtime usage should stay disabled until there is a concrete reason to revive it

## Future Work

### Short-term
- [ ] Keep Hermes CLI memory healthy and low-friction
- [ ] Avoid new Cognee codebase-ingestion experiments unless they directly improve CLI agent memory
- [ ] Only revisit Unity-side Cognee runtime integration after the LocalAI dialogue lane is stable

### Medium-term
- [ ] Decide whether Unity-side Cognee memory should stay disabled permanently or return in a narrower role
- [ ] If direct Cognee API correctness becomes important again, debug dataset isolation and raw recall behavior before adding more content

### Not recommended
- [ ] Using the graph/KG pipeline for code content
- [ ] Treating Cognee as the main codebase-retrieval system for this repo
- [ ] Running multiple concurrent pipeline runs on the same dataset
