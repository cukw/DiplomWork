CREATE TABLE agents (
    id              SERIAL PRIMARY KEY,
    computer_id     INTEGER, -- Убрали внешнюю ссылку на computers из другой БД
    version         VARCHAR(20) NOT NULL,
    status          VARCHAR(20) NOT NULL DEFAULT 'online', -- online / offline / updating
    last_heartbeat  TIMESTAMP,
    config_version  VARCHAR(20),
    offline_since   TIMESTAMP NULL
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
    type            VARCHAR(50) NOT NULL,
    payload_json    TEXT NOT NULL DEFAULT '{}',
    status          VARCHAR(20) NOT NULL DEFAULT 'pending',
    requested_by    VARCHAR(100) NOT NULL DEFAULT 'system',
    result_message  VARCHAR(500) NOT NULL DEFAULT '',
    created_at      TIMESTAMP NOT NULL DEFAULT NOW(),
    acknowledged_at TIMESTAMP NULL
);

CREATE INDEX idx_agent_commands_agent_id ON agent_commands(agent_id);
CREATE INDEX idx_agent_commands_status ON agent_commands(status);
CREATE INDEX idx_agent_commands_agent_status ON agent_commands(agent_id, status);
