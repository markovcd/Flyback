-- Module packs: text the app reads, checked by Validate and published by the admin.
-- pack is the id its header names; modules is each module's id and name joined with char(30), then char(31).
CREATE TABLE packs (
    id TEXT PRIMARY KEY,
    pack TEXT NOT NULL DEFAULT '',
    version TEXT NOT NULL DEFAULT '',
    modules TEXT NOT NULL DEFAULT '',
    sha256 TEXT NOT NULL,
    file_name TEXT NOT NULL,
    size INTEGER NOT NULL,
    submitted_at TEXT NOT NULL,
    downloads INTEGER NOT NULL DEFAULT 0,
    published INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'unchecked',
    reason TEXT,
    search TEXT NOT NULL DEFAULT ''
);
CREATE INDEX packs_submitted ON packs(submitted_at);
CREATE INDEX packs_sha256 ON packs(sha256);
CREATE INDEX packs_pack ON packs(pack);
