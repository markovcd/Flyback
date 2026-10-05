-- The preset site's rows. Files live in R2 under the keys src/keys.ts names.

-- status is unchecked until Validate reads the file, then checked or refused.
CREATE TABLE presets (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    author TEXT,
    description TEXT,
    file_name TEXT NOT NULL,
    size INTEGER NOT NULL,
    submitted_at TEXT NOT NULL,
    downloads INTEGER NOT NULL DEFAULT 0,
    published INTEGER NOT NULL DEFAULT 1,
    status TEXT NOT NULL DEFAULT 'checked',
    reason TEXT,
    search TEXT NOT NULL DEFAULT '',
    lacks TEXT,
    media_state TEXT NOT NULL DEFAULT 'pending',
    media TEXT NOT NULL DEFAULT '',
    media_reason TEXT,
    peaks TEXT
);
CREATE INDEX presets_submitted ON presets(submitted_at);
CREATE INDEX presets_status ON presets(status);

CREATE TABLE preset_tags (
    preset_id TEXT NOT NULL REFERENCES presets(id) ON DELETE CASCADE,
    tag TEXT NOT NULL,
    PRIMARY KEY (preset_id, tag)
);
CREATE INDEX preset_tags_tag ON preset_tags(tag);

CREATE TABLE defaults (
    file_name TEXT PRIMARY KEY,
    preset_id TEXT NOT NULL,
    hash TEXT NOT NULL
);

-- List columns are joined with char(31), and a module is its id and name joined with char(30).
CREATE TABLE plugins (
    id TEXT PRIMARY KEY,
    assembly TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    version TEXT NOT NULL DEFAULT '',
    author TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    adds TEXT NOT NULL DEFAULT '',
    reaches TEXT NOT NULL DEFAULT '',
    builds TEXT NOT NULL DEFAULT '',
    contract TEXT NOT NULL DEFAULT '',
    sha256 TEXT NOT NULL,
    file_name TEXT NOT NULL,
    size INTEGER NOT NULL,
    submitted_at TEXT NOT NULL,
    downloads INTEGER NOT NULL DEFAULT 0,
    published INTEGER NOT NULL DEFAULT 0,
    tags TEXT NOT NULL DEFAULT '',
    preview_type TEXT,
    modules TEXT NOT NULL DEFAULT '',
    signer TEXT,
    signer_fingerprint TEXT,
    status TEXT NOT NULL DEFAULT 'checked',
    reason TEXT,
    search TEXT NOT NULL DEFAULT ''
);
CREATE INDEX plugins_submitted ON plugins(submitted_at);
CREATE INDEX plugins_sha256 ON plugins(sha256);

CREATE TABLE plugin_defaults (
    file_name TEXT PRIMARY KEY,
    plugin_id TEXT NOT NULL,
    hash TEXT NOT NULL
);

CREATE TABLE reports (
    id TEXT PRIMARY KEY,
    kind TEXT NOT NULL,
    subject_id TEXT NOT NULL,
    reason TEXT NOT NULL,
    details TEXT,
    submitted_at TEXT NOT NULL
);
CREATE INDEX reports_subject ON reports(kind, subject_id);

-- A voter is an HMAC of the visitor under rating_key, so the table says nothing about who rated.
CREATE TABLE ratings (
    kind TEXT NOT NULL,
    subject_id TEXT NOT NULL,
    voter TEXT NOT NULL,
    stars INTEGER NOT NULL,
    rated_at TEXT NOT NULL,
    PRIMARY KEY (kind, subject_id, voter)
);

CREATE TABLE rating_key (key BLOB NOT NULL);
INSERT INTO rating_key (key) VALUES (randomblob(32));

CREATE TABLE letters (
    id TEXT PRIMARY KEY,
    mood TEXT NOT NULL,
    message TEXT NOT NULL,
    contact TEXT,
    version TEXT,
    platform TEXT,
    plugins TEXT,
    submitted_at TEXT NOT NULL
);
CREATE INDEX letters_submitted ON letters(submitted_at);

-- One fixed window per visitor and policy; window is its start in seconds since the epoch.
CREATE TABLE limits (
    visitor TEXT NOT NULL,
    policy TEXT NOT NULL,
    window INTEGER NOT NULL,
    count INTEGER NOT NULL,
    PRIMARY KEY (visitor, policy, window)
);
