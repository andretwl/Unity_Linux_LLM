-- Seed data for existing NPC definitions
INSERT INTO npc_definitions (npc_slug, display_name, description, personality_tags, base_mood)
VALUES
    ('butler', 'Butler', 'A stoic and observant butler', ARRAY['formal', 'observant', 'loyal'], 'neutral'),
    ('maid', 'Maid', 'A cheerful and resourceful maid', ARRAY['cheerful', 'resourceful', 'kind'], 'cheerful'),
    ('chef', 'Chef', 'A temperamental but talented chef', ARRAY['temperamental', 'creative', 'proud'], 'neutral'),
    ('game-developer', 'Game Developer', 'A knowledgeable game developer who can discuss Unity, C#, game architecture, and programming topics', ARRAY['technical', 'helpful', 'experienced', 'patient'], 'neutral')
ON CONFLICT (npc_slug) DO NOTHING;
