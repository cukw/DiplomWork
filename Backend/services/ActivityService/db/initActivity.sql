-- Логи активностей
CREATE TABLE activities (
    id              BIGSERIAL PRIMARY KEY,
    computer_id     INTEGER NOT NULL, -- Ссылка на компьютеры в UserService
    timestamp       TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    -- тип активности: 'process_open', 'site_visit', 'file_access', ...
    activity_type   VARCHAR(50) NOT NULL,
    -- детали в JSON: заголовок окна, путь к файлу, т.п.
    details         JSONB,
    duration_ms     INTEGER,
    url             VARCHAR(500),
    process_name    VARCHAR(255),
    is_blocked      BOOLEAN DEFAULT FALSE,
    -- от 0.0 до 100.0, соответствует decimal в коде
    risk_score      NUMERIC(5,2),
    -- флаг, что запись уже синхронизирована с центральным хранилищем / аналитикой
    Synced          BOOLEAN DEFAULT FALSE,
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

CREATE INDEX idx_activities_computer_id ON activities(computer_id);
CREATE INDEX idx_activities_timestamp ON activities(timestamp);
CREATE INDEX idx_activities_activity_type ON activities(activity_type);
CREATE INDEX idx_activities_is_blocked ON activities(is_blocked);
CREATE INDEX idx_activities_risk_score ON activities(risk_score) WHERE risk_score IS NOT NULL;
CREATE INDEX idx_activities_user_id ON activities(user_id);
CREATE INDEX idx_activities_agent_id ON activities(agent_id);
CREATE INDEX idx_activities_event_id ON activities(event_id);
CREATE INDEX idx_activities_batch_id ON activities(batch_id);

-- Архив активностей для retention-политики
CREATE TABLE activities_archive (
    id                  BIGSERIAL PRIMARY KEY,
    original_activity_id BIGINT NOT NULL UNIQUE,
    computer_id         INTEGER NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL,
    activity_type       VARCHAR(50) NOT NULL,
    details             JSONB,
    duration_ms         INTEGER,
    url                 VARCHAR(500),
    process_name        VARCHAR(255),
    is_blocked          BOOLEAN DEFAULT FALSE,
    risk_score          NUMERIC(5,2),
    synced              BOOLEAN DEFAULT FALSE,
    user_id             BIGINT NULL,
    agent_id            BIGINT NULL,
    agent_version       VARCHAR(50),
    device_name         VARCHAR(255),
    collector           VARCHAR(100),
    event_id            VARCHAR(100),
    sequence            BIGINT,
    batch_id            VARCHAR(100),
    source_platform     VARCHAR(50),
    archived_at         TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_activities_archive_original_id ON activities_archive(original_activity_id);
CREATE INDEX idx_activities_archive_computer_id ON activities_archive(computer_id);
CREATE INDEX idx_activities_archive_timestamp ON activities_archive(timestamp);
CREATE INDEX idx_activities_archive_archived_at ON activities_archive(archived_at);

-- Таблица аномалий / странной активности
CREATE TABLE anomalies (
    id              SERIAL PRIMARY KEY,
    activity_id     BIGINT NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
    type            VARCHAR(100) NOT NULL, -- тип аномалии
    description     TEXT,
    detected_at     TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_anomalies_activity_id ON anomalies(activity_id);
CREATE INDEX idx_anomalies_type ON anomalies(type);
CREATE INDEX idx_anomalies_detected_at ON anomalies(detected_at);

-- Transactional outbox для публикации событий в RabbitMQ
CREATE TABLE activity_outbox (
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

CREATE INDEX idx_activity_outbox_pending ON activity_outbox(processed_at, available_at);
CREATE INDEX idx_activity_outbox_activity_id ON activity_outbox(activity_id);
