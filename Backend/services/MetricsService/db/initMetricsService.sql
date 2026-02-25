-- Общая сущность метрики / набора правил
CREATE TABLE metrics (
    id          SERIAL PRIMARY KEY,
    user_id     INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    -- тип метрики: 'process', 'site', 'file', 'generic', etc
    type        VARCHAR(50) NOT NULL,
    -- произвольная конфигурация (JSON): пороги, режимы, дополнительные настройки
    config      JSONB NOT NULL,
    is_active   BOOLEAN DEFAULT TRUE,
    updated_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Вайтлист (разрешённые)
CREATE TABLE whitelists (
    id          SERIAL PRIMARY KEY,
    metric_id   INTEGER NOT NULL REFERENCES metrics(id) ON DELETE CASCADE,
    -- pattern может быть домен, путь к файлу, имя процесса и т.п.
    pattern     VARCHAR(500) NOT NULL,
    -- действие на совпадение, по умолчанию allow
    action      VARCHAR(20) DEFAULT 'allow',
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Блеклист (запрещённые)
CREATE TABLE blacklists (
    id          SERIAL PRIMARY KEY,
    metric_id   INTEGER NOT NULL REFERENCES metrics(id) ON DELETE CASCADE,
    pattern     VARCHAR(500) NOT NULL,
    action      VARCHAR(20) DEFAULT 'block',
    created_at  TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_metrics_user_id ON metrics(user_id);
CREATE INDEX idx_whitelists_metric_id ON whitelists(metric_id);
CREATE INDEX idx_blacklists_metric_id ON blacklists(metric_id);

CREATE TABLE processed_event_inbox (
    id          BIGSERIAL PRIMARY KEY,
    consumer    VARCHAR(128) NOT NULL,
    event_key   VARCHAR(256) NOT NULL,
    message_id  VARCHAR(128),
    processed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_metrics_processed_event_inbox_consumer_event_key
    ON processed_event_inbox(consumer, event_key);

CREATE TABLE activity_event_rollups (
    id               BIGSERIAL PRIMARY KEY,
    bucket_date      DATE NOT NULL,
    computer_id      INTEGER NOT NULL,
    activity_type    VARCHAR(100) NOT NULL,
    total_count      BIGINT NOT NULL DEFAULT 0,
    blocked_count    BIGINT NOT NULL DEFAULT 0,
    risk_score_sum   NUMERIC(18,2) NOT NULL DEFAULT 0,
    risk_score_samples INTEGER NOT NULL DEFAULT 0,
    last_event_at    TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_activity_event_rollups_bucket_computer_type
    ON activity_event_rollups(bucket_date, computer_id, activity_type);

CREATE TABLE anomaly_event_rollups (
    id                BIGSERIAL PRIMARY KEY,
    bucket_date       DATE NOT NULL,
    computer_id       INTEGER NOT NULL,
    anomaly_type      VARCHAR(100) NOT NULL,
    total_count       BIGINT NOT NULL DEFAULT 0,
    high_priority_count BIGINT NOT NULL DEFAULT 0,
    last_event_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_anomaly_event_rollups_bucket_computer_type
    ON anomaly_event_rollups(bucket_date, computer_id, anomaly_type);
