-- Логи активностей
CREATE TABLE activities (
    id              BIGSERIAL PRIMARY KEY,
    computer_id     INTEGER NOT NULL REFERENCES computers(id),
    timestamp       TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    -- тип активности: 'process_open', 'site_visit', 'file_access', ...
    activity_type   VARCHAR(50) NOT NULL,
    -- детали в JSON: заголовок окна, путь к файлу, т.п.
    details         JSONB,
    duration_ms     INTEGER,
    url             VARCHAR(500),
    process_name    VARCHAR(255),
    is_blocked      BOOLEAN DEFAULT FALSE,
    -- от 0.0 до 1.0, например
    risk_score      NUMERIC(3,2),
    -- флаг, что запись уже синхронизирована с центральным хранилищем / аналитикой
    synced          BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_activities_computer_id ON activities(computer_id);
CREATE INDEX idx_activities_timestamp ON activities(timestamp);
CREATE INDEX idx_activities_activity_type ON activities(activity_type);
CREATE INDEX idx_activities_is_blocked ON activities(is_blocked);

-- Таблица аномалий / странной активности
CREATE TABLE anomalies (
    id              SERIAL PRIMARY KEY,
    activity_id     BIGINT NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
    type            VARCHAR(100) NOT NULL, -- тип аномалии
    description     TEXT,
    detected_at     TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_anomalies_activity_id ON anomalies(activity_id);

-- Тест данные
INSERT INTO computers (name, agent_version) VALUES ('PC-001', '1.0');
INSERT INTO activities (computer_id, activity_type, details) VALUES (1, 'process_open', '{"app": "chrome.exe"}');