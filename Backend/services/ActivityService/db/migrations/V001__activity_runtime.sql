CREATE TABLE IF NOT EXISTS activities (
    id              BIGSERIAL PRIMARY KEY,
    computer_id     INTEGER NOT NULL,
    timestamp       TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    activity_type   VARCHAR(50) NOT NULL,
    details         JSONB,
    duration_ms     INTEGER,
    url             VARCHAR(500),
    process_name    VARCHAR(255),
    is_blocked      BOOLEAN DEFAULT FALSE,
    risk_score      NUMERIC(5,2),
    synced          BOOLEAN DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_activities_computer_id ON activities(computer_id);
CREATE INDEX IF NOT EXISTS idx_activities_timestamp ON activities(timestamp);
CREATE INDEX IF NOT EXISTS idx_activities_activity_type ON activities(activity_type);
CREATE INDEX IF NOT EXISTS idx_activities_is_blocked ON activities(is_blocked);
CREATE INDEX IF NOT EXISTS idx_activities_risk_score ON activities(risk_score) WHERE risk_score IS NOT NULL;

CREATE TABLE IF NOT EXISTS anomalies (
    id              SERIAL PRIMARY KEY,
    activity_id     BIGINT NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
    type            VARCHAR(100) NOT NULL,
    description     TEXT,
    detected_at     TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_anomalies_activity_id ON anomalies(activity_id);
CREATE INDEX IF NOT EXISTS idx_anomalies_type ON anomalies(type);
CREATE INDEX IF NOT EXISTS idx_anomalies_detected_at ON anomalies(detected_at);

CREATE TABLE IF NOT EXISTS activity_outbox (
    id              BIGSERIAL PRIMARY KEY,
    event_type      VARCHAR(128) NOT NULL,
    activity_id     BIGINT,
    payload         JSONB NOT NULL,
    headers         JSONB,
    attempt_count   INTEGER NOT NULL DEFAULT 0,
    available_at    TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at    TIMESTAMPTZ,
    last_error      TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_activity_outbox_pending ON activity_outbox(processed_at, available_at);
CREATE INDEX IF NOT EXISTS idx_activity_outbox_activity_id ON activity_outbox(activity_id);

CREATE TABLE IF NOT EXISTS activities_archive (
    id BIGSERIAL PRIMARY KEY,
    original_activity_id BIGINT NOT NULL UNIQUE,
    computer_id INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    activity_type VARCHAR(50) NOT NULL,
    details JSONB NULL,
    duration_ms INTEGER NULL,
    url VARCHAR(500) NULL,
    process_name VARCHAR(255) NULL,
    is_blocked BOOLEAN NOT NULL DEFAULT FALSE,
    risk_score NUMERIC(5,2) NULL,
    synced BOOLEAN NOT NULL DEFAULT FALSE,
    archived_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_activities_archive_original_id
    ON activities_archive(original_activity_id);
CREATE INDEX IF NOT EXISTS idx_activities_archive_computer_id
    ON activities_archive(computer_id);
CREATE INDEX IF NOT EXISTS idx_activities_archive_timestamp
    ON activities_archive(timestamp);
CREATE INDEX IF NOT EXISTS idx_activities_archive_archived_at
    ON activities_archive(archived_at);
