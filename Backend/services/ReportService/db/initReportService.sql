-- Ежедневные агрегированные отчёты по компьютеру
CREATE TABLE daily_reports (
    id                  SERIAL PRIMARY KEY,
    report_date         DATE NOT NULL,
    computer_id         INTEGER, -- Убрали внешнюю ссылку на computers из другой БД
    user_id             INTEGER, -- Убрали внешнюю ссылку на users из другой БД
    total_activities    BIGINT NOT NULL DEFAULT 0,
    blocked_actions     BIGINT NOT NULL DEFAULT 0,
    avg_risk_score      NUMERIC(5,2),
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
CREATE INDEX idx_user_stats_user_id ON user_stats(user_id);