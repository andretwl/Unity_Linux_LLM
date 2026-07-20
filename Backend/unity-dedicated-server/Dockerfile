# NPC Dedicated Server — Dockerfile
# This container runs the Unity IL2CPP dedicated server binary.
# It does NOT bundle LocalAI — it connects to the host's LocalAI via
# Docker host networking (localhost:8080).
#
# BUILD:
#   1. Build the server in Unity: File → Build Settings → Server subtarget
#      → output to Builds/Server1/
#   2. cd <project-root> && docker build -t npc-dedicated-server -f Backend/unity-dedicated-server/Dockerfile .
#
# DEV QUICK START (bind-mount instead of rebuild on every build):
#   docker compose -f Backend/unity-dedicated-server/docker-compose.yml up --build
#
# Or with an explicit bind-mount override:
#   docker run --rm --network host \
#     -v /path/to/your/server/build:/server:rw \
#     npc-dedicated-server

# --- Stage: base (Ubuntu 22.04 — glibc 2.35 ≥ binary's 2.34 requirement) ---
FROM ubuntu:22.04 AS base

ENV DEBIAN_FRONTEND=noninteractive

# Install essential runtime libraries.
# Unity IL2CPP links: libc, libm, libgcc_s, libstdc++.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libc6 \
    libstdc++6 \
    libgcc-s1 \
    libgtk-3-0 \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user for the server process
RUN groupadd -r npc && useradd -r -g npc -d /server -s /sbin/nologin npc && \
    mkdir -p /server && chown npc:npc /server

# --- Stage: build copy (runs as root to handle ownership) ---
FROM base AS with-build

# Copy the server build if it exists — empty directory is fine (COPY won't fail).
COPY Builds/Server1/ /server/

RUN chmod +x /server/*.x86_64 2>/dev/null; chown -R npc:npc /server/; true

# --- Stage: runtime (switch to npc user) ---
FROM base AS runtime

COPY --chmod=755 Backend/unity-dedicated-server/entrypoint.sh /entrypoint.sh

# Copy server files from the build stage (with correct ownership)
COPY --from=with-build --chown=npc:npc /server /server

# Dev bind-mount: run as root so the server can write to /server/
# even when the bind-mount overrides the image's ownership.
# For production, switch back to USER npc with a dedicated volume.
WORKDIR /server

# Unity Transport UDP + TCP (server default port)
EXPOSE 11474/udp
EXPOSE 11474/tcp

ENTRYPOINT ["/entrypoint.sh"]
