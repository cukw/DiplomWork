-- Ежедневные агрегированные отчёты по компьютеру
CREATE TABLE daily_reports (
    id                  SERIAL PRIMARY KEY,
    report_date         DATE NOT NULL,
    computer_id         INTEGER, -- Убрали внешнюю ссылку на computers из другой БД
    user_id             INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    total_activities    BIGINT NOT NULL DEFAULT 0,
    blocked_actions     BIGINT NOT NULL DEFAULT 0,
    avg_risk_score      NUMERIC(5,2),
    anomaly_count       BIGINT NOT NULL DEFAULT 0,
    risk_score_samples  INTEGER NOT NULL DEFAULT 0,
    created_at          TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Статистика пользователя за период
CREATE TABLE user_stats (
    id              SERIAL PRIMARY KEY,
    user_id         INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    period_start    TIMESTAMP NOT NULL,
    period_end      TIMESTAMP NOT NULL,
    total_time_ms   BIGINT,              -- общее "активное" время
    risky_sites     JSONB,               -- массив доменов, например
    violations      INTEGER DEFAULT 0,
    created_at      TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_daily_reports_date ON daily_reports(report_date);
CREATE INDEX idx_daily_reports_computer_id ON daily_reports(computer_id);
CREATE UNIQUE INDEX uq_daily_reports_report_date_computer_id ON daily_reports(report_date, computer_id);
CREATE INDEX idx_user_stats_user_id ON user_stats(user_id);

CREATE TABLE processed_event_inbox (
    id          BIGSERIAL PRIMARY KEY,
    consumer    VARCHAR(128) NOT NULL,
    event_key   VARCHAR(256) NOT NULL,
    message_id  VARCHAR(128),
    processed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_report_processed_event_inbox_consumer_event_key
    ON processed_event_inbox(consumer, event_key);

CREATE TABLE report_daily_anomaly_rollups (
    id           BIGSERIAL PRIMARY KEY,
    bucket_date  DATE NOT NULL,
    computer_id  INTEGER NOT NULL,
    anomaly_type VARCHAR(100) NOT NULL,
    total_count  BIGINT NOT NULL DEFAULT 0,
    last_event_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_report_daily_anomaly_rollups_bucket_computer_type
    ON report_daily_anomaly_rollups(bucket_date, computer_id, anomaly_type);
