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
    synced          BOOLEAN DEFAULT FALSE,
    user_id         BIGINT NULL,
    agent_id        BIGINT NULL,
    agent_version   VARCHAR(50),
    device_name     VARCHAR(255),
    collector       VARCHAR(100),
    event_id        VARCHAR(100),
    sequence        BIGINT,
    batch_id        VARCHAR(100),
    source_platform VARCHAR(50)
);

ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS user_id BIGINT NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS agent_id BIGINT NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS agent_version VARCHAR(50) NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS device_name VARCHAR(255) NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS collector VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS event_id VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS sequence BIGINT NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS batch_id VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities ADD COLUMN IF NOT EXISTS source_platform VARCHAR(50) NULL;

CREATE INDEX IF NOT EXISTS idx_activities_computer_id ON activities(computer_id);
CREATE INDEX IF NOT EXISTS idx_activities_timestamp ON activities(timestamp);
CREATE INDEX IF NOT EXISTS idx_activities_activity_type ON activities(activity_type);
CREATE INDEX IF NOT EXISTS idx_activities_is_blocked ON activities(is_blocked);
CREATE INDEX IF NOT EXISTS idx_activities_risk_score ON activities(risk_score) WHERE risk_score IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_activities_user_id ON activities(user_id);
CREATE INDEX IF NOT EXISTS idx_activities_agent_id ON activities(agent_id);
CREATE INDEX IF NOT EXISTS idx_activities_event_id ON activities(event_id);
CREATE INDEX IF NOT EXISTS idx_activities_batch_id ON activities(batch_id);

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
    user_id BIGINT NULL,
    agent_id BIGINT NULL,
    agent_version VARCHAR(50) NULL,
    device_name VARCHAR(255) NULL,
    collector VARCHAR(100) NULL,
    event_id VARCHAR(100) NULL,
    sequence BIGINT NULL,
    batch_id VARCHAR(100) NULL,
    source_platform VARCHAR(50) NULL,
    archived_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS user_id BIGINT NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS agent_id BIGINT NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS agent_version VARCHAR(50) NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS device_name VARCHAR(255) NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS collector VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS event_id VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS sequence BIGINT NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS batch_id VARCHAR(100) NULL;
ALTER TABLE IF EXISTS activities_archive ADD COLUMN IF NOT EXISTS source_platform VARCHAR(50) NULL;

CREATE INDEX IF NOT EXISTS idx_activities_archive_original_id
    ON activities_archive(original_activity_id);
CREATE INDEX IF NOT EXISTS idx_activities_archive_computer_id
    ON activities_archive(computer_id);
CREATE INDEX IF NOT EXISTS idx_activities_archive_timestamp
    ON activities_archive(timestamp);
CREATE INDEX IF NOT EXISTS idx_activities_archive_archived_at
    ON activities_archive(archived_at);
