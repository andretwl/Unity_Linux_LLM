CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS players (
    player_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    username VARCHAR(32) NOT NULL,
    username_normalized VARCHAR(32) NOT NULL UNIQUE,
    password_hash BYTEA NOT NULL,
    password_salt BYTEA NOT NULL,
    password_iterations INTEGER NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc', now()),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc', now())
);

CREATE TABLE IF NOT EXISTS player_sessions (
    session_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id UUID NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    session_token_hash TEXT NOT NULL UNIQUE,
    device_id TEXT,
    remember_me BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc', now()),
    expires_at_utc TIMESTAMPTZ NOT NULL,
    last_seen_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc', now()),
    revoked_at_utc TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_player_sessions_player_id ON player_sessions(player_id);
CREATE INDEX IF NOT EXISTS idx_player_sessions_expires_at ON player_sessions(expires_at_utc);
CREATE INDEX IF NOT EXISTS idx_player_sessions_revoked_at ON player_sessions(revoked_at_utc);
