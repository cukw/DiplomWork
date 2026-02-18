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
    synced          BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_activities_computer_id ON activities(computer_id);
CREATE INDEX idx_activities_timestamp ON activities(timestamp);
CREATE INDEX idx_activities_activity_type ON activities(activity_type);
CREATE INDEX idx_activities_is_blocked ON activities(is_blocked);
CREATE INDEX idx_activities_risk_score ON activities(risk_score) WHERE risk_score IS NOT NULL;

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

-- Тест данные (предполагаем, что компьютер с ID=1 существует в UserService)
INSERT INTO activities (computer_id, activity_type, details, risk_score) VALUES
    (1, 'process_open', '{"app": "chrome.exe"}', 10.5),
    (1, 'site_visit', '{"url": "https://example.com"}', 5.0),
    (2, 'file_access', '{"path": "/etc/passwd"}', 85.0);