CREATE TABLE agents (
    id              SERIAL PRIMARY KEY,
    computer_id     INTEGER, -- Убрали внешнюю ссылку на computers из другой БД
    version         VARCHAR(20) NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'online', -- online / offline / updating
    last_heartbeat  TIMESTAMP,
    config_version  VARCHAR(20),
    offline_since   TIMESTAMP NULL,
    desired_version VARCHAR(20),
    desired_version_set_at TIMESTAMP,
    health_json TEXT NOT NULL DEFAULT '{}',
    queue_size INTEGER NOT NULL DEFAULT 0,
    last_collected_at TIMESTAMP NULL,
    last_sent_at TIMESTAMP NULL,
    last_error VARCHAR(500) NOT NULL DEFAULT '',
    policy_version VARCHAR(50) NULL,
    capabilities_json TEXT NOT NULL DEFAULT '{}',
    collector_statuses_json TEXT NOT NULL DEFAULT '{}',
    source_platform VARCHAR(50) NULL
);

CREATE TABLE sync_batches (
    id              SERIAL PRIMARY KEY,
    agent_id        INTEGER NOT NULL REFERENCES agents(id),
    batch_id        VARCHAR(100) NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'pending', -- pending / success / failed
    synced_at       TIMESTAMP,
    records_count   INTEGER DEFAULT 0
);

CREATE UNIQUE INDEX idx_agents_computer_id ON agents(computer_id);
CREATE INDEX idx_sync_batches_agent_id ON sync_batches(agent_id);
CREATE INDEX idx_sync_batches_batch_id ON sync_batches(batch_id);

ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS health_json TEXT NOT NULL DEFAULT '{}';
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS queue_size INTEGER NOT NULL DEFAULT 0;
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS last_collected_at TIMESTAMP NULL;
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS last_sent_at TIMESTAMP NULL;
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS last_error VARCHAR(500) NOT NULL DEFAULT '';
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS policy_version VARCHAR(50) NULL;
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS capabilities_json TEXT NOT NULL DEFAULT '{}';
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS collector_statuses_json TEXT NOT NULL DEFAULT '{}';
ALTER TABLE IF EXISTS agents ADD COLUMN IF NOT EXISTS source_platform VARCHAR(50) NULL;

CREATE TABLE agent_policies (
    id                              SERIAL PRIMARY KEY,
    agent_id                        INTEGER NOT NULL UNIQUE REFERENCES agents(id) ON DELETE CASCADE,
    computer_id                     INTEGER NOT NULL,
    policy_version                  VARCHAR(50) NOT NULL DEFAULT '1',
    collection_interval_sec         INTEGER NOT NULL DEFAULT 5,
    heartbeat_interval_sec          INTEGER NOT NULL DEFAULT 15,
    flush_interval_sec              INTEGER NOT NULL DEFAULT 5,
    enable_process_collection       BOOLEAN NOT NULL DEFAULT TRUE,
    enable_browser_collection       BOOLEAN NOT NULL DEFAULT TRUE,
    enable_active_window_collection BOOLEAN NOT NULL DEFAULT TRUE,
    enable_idle_collection          BOOLEAN NOT NULL DEFAULT TRUE,
    idle_threshold_sec              INTEGER NOT NULL DEFAULT 120,
    browser_poll_interval_sec       INTEGER NOT NULL DEFAULT 10,
    process_snapshot_limit          INTEGER NOT NULL DEFAULT 50,
    high_risk_threshold             REAL NOT NULL DEFAULT 85,
    auto_lock_enabled               BOOLEAN NOT NULL DEFAULT TRUE,
    admin_blocked                   BOOLEAN NOT NULL DEFAULT FALSE,
    blocked_reason                  VARCHAR(500) NULL,
    browsers_json                   TEXT NOT NULL DEFAULT '["chrome","edge","firefox"]',
    updated_at                      TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_agent_policies_computer_id ON agent_policies(computer_id);

CREATE TABLE agent_commands (
    id              SERIAL PRIMARY KEY,
    agent_id        INTEGER NOT NULL REFERENCES agents(id) ON DELETE CASCADE,
    command_key     VARCHAR(100) NOT NULL,
    type            VARCHAR(50) NOT NULL,
    payload_json    TEXT NOT NULL DEFAULT '{}',
    status          VARCHAR(20) NOT NULL DEFAULT 'pending',
    requested_by    VARCHAR(100) NOT NULL DEFAULT 'system',
    result_message  VARCHAR(500) NOT NULL DEFAULT '',
    delivery_attempts INTEGER NOT NULL DEFAULT 0,
    max_delivery_attempts INTEGER NOT NULL DEFAULT 5,
    last_dispatch_at TIMESTAMP NULL,
    next_retry_at TIMESTAMP NULL,
    timeout_at TIMESTAMP NULL,
    dead_letter_reason VARCHAR(500) NOT NULL DEFAULT '',
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    acknowledged_at TIMESTAMP NULL
);

CREATE INDEX idx_agent_commands_agent_id ON agent_commands(agent_id);
CREATE INDEX idx_agent_commands_status ON agent_commands(status);
CREATE INDEX idx_agent_commands_agent_status ON agent_commands(agent_id, status);
CREATE UNIQUE INDEX uq_agent_commands_agent_command_key ON agent_commands(agent_id, command_key);
CREATE INDEX idx_agent_commands_timeout_at ON agent_commands(timeout_at);
CREATE INDEX idx_agent_commands_next_retry_at ON agent_commands(next_retry_at);

CREATE TABLE agent_command_dlq (
    id              SERIAL PRIMARY KEY,
    agent_command_id INTEGER NOT NULL UNIQUE REFERENCES agent_commands(id) ON DELETE CASCADE,
    agent_id        INTEGER NOT NULL,
    command_key     VARCHAR(100) NOT NULL,
    type            VARCHAR(50) NOT NULL,
    payload_json    TEXT NOT NULL DEFAULT '{}',
    reason          VARCHAR(500) NOT NULL DEFAULT '',
    delivery_attempts INTEGER NOT NULL DEFAULT 0,
    failed_at       TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_agent_command_dlq_agent_id ON agent_command_dlq(agent_id);
CREATE INDEX idx_agent_command_dlq_failed_at ON agent_command_dlq(failed_at);
