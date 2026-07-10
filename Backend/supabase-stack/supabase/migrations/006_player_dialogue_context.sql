-- ============================================================
-- Migration 006: Player dialogue context RPC
-- ============================================================
-- Returns a single JSON payload containing everything needed to
-- enrich an NPC dialogue system prompt — player profile,
-- relationship with the target NPC, and investigation state
-- (clues, items, locations).
-- ============================================================

-- Ensure pgcrypto is available (used by player_profiles FK)
-- (already enabled in migration 005)

-- ============================================================
-- RPC: get_player_dialogue_context
--
-- Usage:
--   SELECT * FROM get_player_dialogue_context(
--     p_user_id := '...',
--     p_npc_slug := 'butler'
--   );
--
-- Returns a single JSON object:
-- {
--   "player_name": "...",
--   "trust_score": 50,
--   "current_mood": "neutral",
--   "dialogue_count": 12,
--   "clues": ["...", "..."],
--   "items": ["...", "..."],
--   "locations": ["...", "..."],
--   "last_interaction_at": "2026-07-10T12:00:00Z"
-- }
-- ============================================================
create or replace function get_player_dialogue_context(
    p_user_id uuid,
    p_npc_slug text default null
)
returns jsonb
language plpgsql
stable
as $$
declare
    v_profile jsonb;
    v_relationship jsonb;
    v_clues jsonb;
    v_items jsonb;
    v_locations jsonb;
begin
    -- 1. Player profile
    select jsonb_build_object(
        'player_name', coalesce(display_name, ''),
        'player_id', user_id::text,
        'is_online', is_online
    ) into v_profile
    from player_profiles
    where user_id = p_user_id;

    if v_profile is null then
        v_profile := jsonb_build_object(
            'player_name', '',
            'player_id', p_user_id::text,
            'is_online', false
        );
    end if;

    -- 2. NPC relationship (if npc_slug provided)
    if p_npc_slug is not null then
        select jsonb_build_object(
            'trust_score', trust_score,
            'current_mood', current_mood,
            'dialogue_count', dialogue_count,
            'last_interaction_at', last_interaction_at
        ) into v_relationship
        from player_npc_relationships
        where user_id = p_user_id
          and npc_slug = p_npc_slug;

        if v_relationship is null then
            v_relationship := jsonb_build_object(
                'trust_score', 50,
                'current_mood', 'neutral',
                'dialogue_count', 0,
                'last_interaction_at', null
            );
        end if;
    else
        v_relationship := '{}'::jsonb;
    end if;

    -- 3. Clues discovered by this player
    select coalesce(
        jsonb_agg(clue_text order by discovered_at asc),
        '[]'::jsonb
    ) into v_clues
    from player_clues
    where user_id = p_user_id;

    -- 4. Items obtained by this player
    select coalesce(
        jsonb_agg(item_name order by acquired_at asc),
        '[]'::jsonb
    ) into v_items
    from player_items
    where user_id = p_user_id;

    -- 5. Locations visited by this player
    select coalesce(
        jsonb_agg(location_name order by visited_at asc),
        '[]'::jsonb
    ) into v_locations
    from player_locations
    where user_id = p_user_id;

    -- Combine
    return v_profile || v_relationship
        || jsonb_build_object(
            'clues', v_clues,
            'items', v_items,
            'locations', v_locations
        );
end;
$$;
