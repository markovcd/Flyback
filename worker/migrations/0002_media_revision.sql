-- Counts the writes to a preset's media; the media URLs carry it, so a render is cached for good under its own URL.
ALTER TABLE presets ADD COLUMN media_rev INTEGER NOT NULL DEFAULT 0;
