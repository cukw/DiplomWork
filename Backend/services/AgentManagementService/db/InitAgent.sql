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